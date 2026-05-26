#!/bin/bash
# ============================================================
# generate_img.sh
# Generates a flashable sdcard.img from BitBake build outputs.
#
# Uses mtools to write boot files directly into the FAT
# partition of the WIC image without needing mount or loop
# devices. Works inside Docker without --privileged.
#
# Usage:
#   sh generate_img.sh
#
# Output:
#   /home/edf/projects/EDF/sdcard.img
#   /home/edf/projects/EDF/sdcard.img.bmap
#
# Flash with Balena Etcher:
#   Open Etcher → Select sdcard.img → Select SD card → Flash
#
# Flash with bmaptool (faster):
#   sudo bmaptool copy sdcard.img /dev/sdX
#
# Flash with dd (fallback):
#   sudo dd if=sdcard.img of=/dev/sdX bs=4M status=progress
# ============================================================

set -e

# ── Colours ───────────────────────────────────────────────────
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m'

log_info()    { echo -e "${BLUE}[INFO]${NC}  $1"; }
log_success() { echo -e "${GREEN}[OK]${NC}    $1"; }
log_warn()    { echo -e "${YELLOW}[WARN]${NC}  $1"; }
log_error()   { echo -e "${RED}[ERROR]${NC} $1"; exit 1; }

# ── Configuration ─────────────────────────────────────────────
BUILD_DIR="/home/edf/projects/EDF/build"
OUTPUT_DIR="/home/edf/projects/EDF"
MACHINE="zynqmp-zcu104-sdt-full"
IMAGE_NAME="core-image-minimal"
OUTPUT_IMG="${OUTPUT_DIR}/sdcard.img"
OUTPUT_BMAP="${OUTPUT_DIR}/sdcard.img.bmap"

DEPLOY="${BUILD_DIR}/tmp/deploy/images/${MACHINE}"

# Tell mtools not to check geometry constraints on image files
export MTOOLS_SKIP_CHECK=1

# ── Check required tools ──────────────────────────────────────
check_tools() {
    log_info "Checking required tools..."

    for tool in dd fdisk mcopy mdir; do
        command -v "$tool" >/dev/null 2>&1 || \
            log_error "${tool} not found.
        Install: sudo apt-get install -y mtools fdisk
        On Arch: sudo pacman -S mtools util-linux"
    done

    command -v bmaptool >/dev/null 2>&1 && \
        HAVE_BMAPTOOL=1 || HAVE_BMAPTOOL=0

    log_success "Tools OK (using mtools — no mount/loop devices needed)"
}

# ── Locate build outputs ──────────────────────────────────────
find_outputs() {
    log_info "Locating build outputs..."

    # WIC image — exclude qemu variants
    WIC_IMAGE=$(ls "${DEPLOY}/${IMAGE_NAME}"*.rootfs.wic \
                   "${DEPLOY}/${IMAGE_NAME}"*.wic \
                   2>/dev/null | grep -v qemu | head -1)

    [ -n "$WIC_IMAGE" ] && [ -f "$WIC_IMAGE" ] || \
        log_error "WIC image not found in ${DEPLOY}/
        Run: MACHINE=${MACHINE} bitbake ${IMAGE_NAME}"

    # BOOT.BIN — handle all naming variants
    BOOT_BIN=$(ls "${DEPLOY}/BOOT.BIN" \
                  "${DEPLOY}/BOOT-${MACHINE}"*.bin \
                  "${DEPLOY}/boot.bin" \
                  2>/dev/null | head -1)

    [ -n "$BOOT_BIN" ] && [ -f "$BOOT_BIN" ] || \
        log_error "BOOT.BIN not found in ${DEPLOY}/
        Run: MACHINE=${MACHINE} bitbake xilinx-bootbin"

    # Kernel
    KERNEL="${DEPLOY}/Image"
    [ -f "$KERNEL" ] || \
        log_error "Kernel Image not found in ${DEPLOY}/"

    # Device tree — find non-empty file
    DTB=$(ls "${DEPLOY}/system.dtb" \
             "${DEPLOY}/${MACHINE}-system.dtb" \
             "${DEPLOY}/${MACHINE}-system-"*.dtb \
             2>/dev/null | head -1)

    [ -n "$DTB" ] || \
        log_error "system.dtb not found in ${DEPLOY}/"

    # If symlink resolves to empty file, find the real timestamped one
    DTB_SIZE=$(stat -L -c%s "$DTB" 2>/dev/null || echo 0)
    if [ "$DTB_SIZE" -eq 0 ]; then
        log_warn "system.dtb is empty — searching for timestamped DTB..."
        DTB=$(ls "${DEPLOY}/${MACHINE}-system-"*.dtb 2>/dev/null | \
            xargs -I{} stat -L -c "%s {}" {} 2>/dev/null | \
            sort -rn | head -1 | awk '{print $2}')
        [ -n "$DTB" ] && [ -f "$DTB" ] || \
            log_error "Could not find a non-empty system.dtb"
        log_info "Using DTB: ${DTB}"
    fi

    # boot.scr (optional)
    BOOTSCR=""
    [ -f "${DEPLOY}/boot.scr" ] && BOOTSCR="${DEPLOY}/boot.scr"

    log_success "All build outputs found"
    echo ""
    log_info "  WIC image : ${WIC_IMAGE}"
    log_info "  BOOT.BIN  : ${BOOT_BIN} ($(du -sh ${BOOT_BIN} | cut -f1))"
    log_info "  Kernel    : ${KERNEL} ($(du -sh ${KERNEL} | cut -f1))"
    log_info "  DTB       : ${DTB} ($(du -sh ${DTB} | cut -f1))"
    [ -n "$BOOTSCR" ] && \
        log_info "  boot.scr  : ${BOOTSCR}"
}

# ── Get image size ────────────────────────────────────────────
get_image_size() {
    WIC_SIZE_BYTES=$(stat -c%s "$WIC_IMAGE")
    WIC_SIZE_MB=$(( WIC_SIZE_BYTES / 1024 / 1024 + 1 ))
    log_info "WIC image size: ${WIC_SIZE_MB} MB"
}

# ── Check disk space ──────────────────────────────────────────
check_space() {
    AVAILABLE=$(df "${OUTPUT_DIR}" | awk 'NR==2 {print int($4/1024)}')
    WIC_MB=$(du -sm "${WIC_IMAGE}" | cut -f1)
    NEEDED=$(( WIC_MB + 100 ))

    [ "$AVAILABLE" -ge "$NEEDED" ] || \
        log_error "Insufficient space: ${AVAILABLE}MB available, ~${NEEDED}MB needed"

    log_info "Disk space OK: ${AVAILABLE}MB available"
}

# ── Get BOOT partition offset ─────────────────────────────────
get_boot_offset() {
    log_info "Locating BOOT partition..."

    FDISK_OUT=$(fdisk -l "${OUTPUT_IMG}" 2>/dev/null)

    # Parse start sector — handle optional boot flag (*) in column 2
    # With flag:    img1  *  8  ...  W95 FAT32
    # Without flag: img1     8  ...  W95 FAT32
    BOOT_START=$(echo "$FDISK_OUT" | \
        awk '/FAT|fat|W95/{
            if ($2 == "*") print $3
            else print $2
        }' | head -1)

    if [ -z "$BOOT_START" ] || [ "$BOOT_START" = "*" ]; then
        log_warn "Partition auto-detection failed — using default sector 8"
        BOOT_START=8
    fi

    BOOT_OFFSET_BYTES=$(( BOOT_START * 512 ))

    log_info "BOOT partition: sector ${BOOT_START}, offset ${BOOT_OFFSET_BYTES} bytes"
}

# ── Build the SD card image ───────────────────────────────────
build_image() {
    log_info "Building SD card image..."
    echo ""

    # ── Step 1: Copy WIC as base ──────────────────────────────
    log_info "[1/4] Copying WIC image as base..."
    cp "${WIC_IMAGE}" "${OUTPUT_IMG}"
    log_success "Base image copied ($(du -sh ${OUTPUT_IMG} | cut -f1))"

    # ── Step 2: Locate BOOT partition ─────────────────────────
    log_info "[2/4] Locating BOOT partition..."
    get_boot_offset

    # mtools image spec with partition offset
    # Format: image@@offset_in_bytes
    MTOOLS_IMG="${OUTPUT_IMG}@@${BOOT_OFFSET_BYTES}"

    # ── Step 3: Write boot files via mtools ───────────────────
    log_info "[3/4] Writing ZCU104 boot files via mtools..."
    log_info "      (no mount or loop devices required)"
    echo ""

    log_info "  Writing BOOT.BIN..."
    mcopy -i "${MTOOLS_IMG}" -o "${BOOT_BIN}" ::BOOT.BIN
    log_success "  BOOT.BIN written ($(du -sh ${BOOT_BIN} | cut -f1))"

    log_info "  Writing Image (kernel)..."
    mcopy -i "${MTOOLS_IMG}" -o "${KERNEL}" ::Image
    log_success "  Image written ($(du -sh ${KERNEL} | cut -f1))"

    log_info "  Writing system.dtb..."
    mcopy -i "${MTOOLS_IMG}" -o "${DTB}" ::system.dtb
    log_success "  system.dtb written ($(du -sh ${DTB} | cut -f1))"

    if [ -n "$BOOTSCR" ]; then
        log_info "  Writing boot.scr..."
        mcopy -i "${MTOOLS_IMG}" -o "${BOOTSCR}" ::boot.scr
        log_success "  boot.scr written"
    fi

    echo ""
    log_info "  Final BOOT partition contents:"
    mdir -i "${MTOOLS_IMG}" :: | sed 's/^/    /'

    log_success "Boot files written successfully"

    # ── Step 4: Generate bmap ─────────────────────────────────
    log_info "[4/4] Generating bmap file..."

    if [ "$HAVE_BMAPTOOL" = "1" ]; then
        bmaptool create "${OUTPUT_IMG}" > "${OUTPUT_BMAP}"
        log_success "bmap file generated: ${OUTPUT_BMAP}"
    else
        log_warn "bmaptool not found — skipping bmap generation"
        log_warn "Install: sudo apt-get install -y bmap-tools"
    fi
}

# ── Print summary ─────────────────────────────────────────────
print_summary() {
    echo ""
    echo "============================================"
    echo " SD Card Image Ready"
    echo "============================================"
    echo ""
    log_success "Image : ${OUTPUT_IMG}"
    echo "        Size  : $(du -sh ${OUTPUT_IMG} | cut -f1)"
    [ -f "$OUTPUT_BMAP" ] && \
        log_success "bmap  : ${OUTPUT_BMAP}"
    echo ""
    echo " Flash options:"
    echo ""
    echo " 1. Balena Etcher (Windows/macOS/Linux GUI):"
    echo "    Open Etcher → Select sdcard.img → Flash"
    echo ""
    echo " 2. bmaptool (fast, Linux):"
    echo "    sudo bmaptool copy ${OUTPUT_IMG} /dev/sdX"
    echo ""
    echo " 3. dd (fallback):"
    echo "    sudo dd if=${OUTPUT_IMG} of=/dev/sdX \\"
    echo "        bs=4M status=progress conv=fsync"
    echo ""
    echo " ZCU104 boot mode switches (SW6):"
    echo "    1=OFF 2=OFF 3=OFF 4=ON  → SD card boot"
    echo ""
    echo " Serial console: 115200 baud, /dev/ttyUSB1"
    echo "============================================"
}

# ── Main ──────────────────────────────────────────────────────
echo ""
echo "============================================"
echo " ZCU104 DDR4 TestBench SD Card Generator"
echo "============================================"
echo ""

check_tools
find_outputs
get_image_size
check_space
build_image
print_summary
