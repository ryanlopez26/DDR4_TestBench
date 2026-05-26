#!/bin/bash
# ============================================================
# generate_img.sh
# Generates a flashable sdcard.img from BitBake build outputs.
# The image can be flashed using Balena Etcher or dd.
#
# Must be run from inside the EDF build directory or with
# paths adjusted below.
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

# ── Check required tools ──────────────────────────────────────
check_tools() {
    log_info "Checking required tools..."

    for tool in dd fdisk mkfs.vfat mcopy; do
        command -v "$tool" >/dev/null 2>&1 || \
            log_error "${tool} not found. Install: sudo apt-get install -y dosfstools mtools fdisk"
    done

    command -v bmaptool >/dev/null 2>&1 && \
        HAVE_BMAPTOOL=1 || HAVE_BMAPTOOL=0

    log_success "Tools OK"
}

# ── Locate build outputs ──────────────────────────────────────
find_outputs() {
    log_info "Locating build outputs..."

    # Find WIC image — match both .wic and .rootfs.wic patterns
    WIC_IMAGE=$(ls "${DEPLOY}/${IMAGE_NAME}"*.rootfs.wic \
                   "${DEPLOY}/${IMAGE_NAME}"*.wic \
                   2>/dev/null | grep -v qemu | head -1)

    if [ -z "$WIC_IMAGE" ] || [ ! -f "$WIC_IMAGE" ]; then
        log_error "WIC image not found in ${DEPLOY}/
        Run: MACHINE=${MACHINE} bitbake ${IMAGE_NAME}"
    fi

    # Find BOOT.BIN — handle both BOOT.BIN and BOOT-<machine>.bin naming
    BOOT_BIN=$(ls "${DEPLOY}/BOOT.BIN" \
                  "${DEPLOY}/BOOT-${MACHINE}*.bin" \
                  "${DEPLOY}/boot.bin" \
                  2>/dev/null | head -1)

    if [ -z "$BOOT_BIN" ] || [ ! -f "$BOOT_BIN" ]; then
        log_error "BOOT.BIN not found in ${DEPLOY}/
        Run: MACHINE=${MACHINE} bitbake xilinx-bootbin"
    fi

    # Find kernel Image
    KERNEL="${DEPLOY}/Image"
    [ -f "$KERNEL" ] || \
        log_error "Kernel Image not found in ${DEPLOY}/"

    # Find device tree — try symlink first then timestamped
    DTB=$(ls "${DEPLOY}/system.dtb" \
             "${DEPLOY}/${MACHINE}-system.dtb" \
             "${DEPLOY}/${MACHINE}-system-*.dtb" \
             2>/dev/null | head -1)

    if [ -z "$DTB" ] || [ ! -f "$DTB" ]; then
        log_error "system.dtb not found in ${DEPLOY}/"
    fi

    # DTB size sanity check — symlinks to empty files will be 0
    DTB_SIZE=$(stat -c%s "$DTB" 2>/dev/null || echo 0)
    if [ "$DTB_SIZE" -eq 0 ]; then
        log_warn "system.dtb symlink appears empty — searching for real DTB..."
        DTB=$(ls "${DEPLOY}/${MACHINE}-system-"*.dtb 2>/dev/null | head -1)
        DTB_SIZE=$(stat -c%s "$DTB" 2>/dev/null || echo 0)
        if [ "$DTB_SIZE" -eq 0 ]; then
            log_error "Could not find a non-empty system.dtb in ${DEPLOY}/"
        fi
        log_info "Using: ${DTB}"
    fi

    # Find boot.scr (optional)
    BOOTSCR="${DEPLOY}/boot.scr"

    log_success "All build outputs found"
    echo ""
    log_info "  WIC image : ${WIC_IMAGE}"
    log_info "  BOOT.BIN  : ${BOOT_BIN} ($(du -sh ${BOOT_BIN} | cut -f1))"
    log_info "  Kernel    : ${KERNEL} ($(du -sh ${KERNEL} | cut -f1))"
    log_info "  DTB       : ${DTB} ($(du -sh ${DTB} | cut -f1))"
    [ -f "$BOOTSCR" ] && log_info "  boot.scr  : ${BOOTSCR}"
}

# ── Get WIC image size ────────────────────────────────────────
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

    if [ "$AVAILABLE" -lt "$NEEDED" ]; then
        log_error "Insufficient space in ${OUTPUT_DIR}: \
${AVAILABLE}MB available, ~${NEEDED}MB needed"
    fi

    log_info "Disk space OK: ${AVAILABLE}MB available"
}

# ── Build the SD card image ───────────────────────────────────
build_image() {
    log_info "Building SD card image..."
    echo ""

    WORK_DIR=$(mktemp -d)
    # Ensure cleanup on exit
    trap "sudo umount ${WORK_DIR}/boot 2>/dev/null; \
          [ -n \"$LOOP_DEV\" ] && sudo losetup -d $LOOP_DEV 2>/dev/null; \
          rm -rf ${WORK_DIR}" EXIT

    # ── Step 1: Copy WIC as base ──────────────────────────────
    log_info "[1/4] Copying WIC image as base..."
    cp "${WIC_IMAGE}" "${OUTPUT_IMG}"
    log_success "Base image copied ($(du -sh ${OUTPUT_IMG} | cut -f1))"

    # ── Step 2: Locate BOOT partition ─────────────────────────
    log_info "[2/4] Locating BOOT partition in image..."

    FDISK_OUT=$(fdisk -l "${OUTPUT_IMG}" 2>/dev/null)
    echo "$FDISK_OUT"
    echo ""

    # Parse start sector — handle optional boot flag (*) in column 2
    # With boot flag:    /dev/sdX1  *  8  ...  W95 FAT32
    # Without boot flag: /dev/sdX1     8  ...  W95 FAT32
    BOOT_START=$(echo "$FDISK_OUT" | \
        awk '/FAT|fat|W95/{
            if ($2 == "*") print $3
            else print $2
        }' | head -1)

    if [ -z "$BOOT_START" ] || [ "$BOOT_START" = "*" ]; then
        log_warn "Auto-detection failed — using default offset 8 sectors"
        BOOT_START=8
    fi

    BOOT_OFFSET=$(( BOOT_START * 512 ))
    log_info "BOOT partition: sector ${BOOT_START}, offset ${BOOT_OFFSET} bytes"

    # ── Step 3: Mount and replace boot files ──────────────────
    log_info "[3/4] Replacing boot files with ZCU104-specific versions..."

    MOUNT_POINT="${WORK_DIR}/boot"
    mkdir -p "${MOUNT_POINT}"
    LOOP_DEV=""

    # Try offset mount first, fall back to loopback device
    if sudo mount -o loop,offset=${BOOT_OFFSET} \
            "${OUTPUT_IMG}" "${MOUNT_POINT}" 2>/dev/null; then
        log_info "Mounted via loop offset"
    else
        log_info "Offset mount failed — trying loopback device..."
        LOOP_DEV=$(sudo losetup -f --show -P "${OUTPUT_IMG}")
        sudo mount "${LOOP_DEV}p1" "${MOUNT_POINT}" || \
            log_error "Failed to mount BOOT partition from image"
        log_info "Mounted via ${LOOP_DEV}p1"
    fi

    log_info "Current BOOT partition contents:"
    ls -lh "${MOUNT_POINT}" | sed 's/^/    /'
    echo ""

    # Copy ZCU104-specific boot files
    sudo cp "${BOOT_BIN}"  "${MOUNT_POINT}/BOOT.BIN"
    sudo cp "${KERNEL}"    "${MOUNT_POINT}/Image"
    sudo cp "${DTB}"       "${MOUNT_POINT}/system.dtb"
    [ -f "$BOOTSCR" ] && sudo cp "${BOOTSCR}" "${MOUNT_POINT}/boot.scr"

    log_info "Updated BOOT partition contents:"
    ls -lh "${MOUNT_POINT}" | sed 's/^/    /'

    sudo umount "${MOUNT_POINT}"

    if [ -n "$LOOP_DEV" ]; then
        sudo losetup -d "${LOOP_DEV}"
        LOOP_DEV=""
    fi

    log_success "Boot files replaced"

    # ── Step 4: Generate bmap ─────────────────────────────────
    log_info "[4/4] Generating bmap file..."

    if [ "$HAVE_BMAPTOOL" = "1" ]; then
        bmaptool create "${OUTPUT_IMG}" > "${OUTPUT_BMAP}"
        log_success "bmap file generated: ${OUTPUT_BMAP}"
    else
        log_warn "bmaptool not found — skipping bmap generation"
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
    echo "        Size : $(du -sh ${OUTPUT_IMG} | cut -f1)"
    [ -f "$OUTPUT_BMAP" ] && log_success "bmap  : ${OUTPUT_BMAP}"
    echo ""
    echo " Flash options:"
    echo ""
    echo " 1. Balena Etcher (GUI, Windows/macOS/Linux):"
    echo "    Open Etcher → Select sdcard.img → Flash"
    echo ""
    echo " 2. bmaptool (fast, Linux):"
    echo "    sudo bmaptool copy ${OUTPUT_IMG} /dev/sdX"
    echo ""
    echo " 3. dd (fallback):"
    echo "    sudo dd if=${OUTPUT_IMG} of=/dev/sdX \\"
    echo "        bs=4M status=progress conv=fsync"
    echo ""
    echo " ZCU104 boot mode (SW6):"
    echo "    1=OFF 2=OFF 3=OFF 4=ON  (SD card)"
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