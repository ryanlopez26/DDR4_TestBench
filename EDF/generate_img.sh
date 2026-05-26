#!/bin/bash
# ============================================================
# generate-sdcard-img.sh
# Generates a flashable sdcard.img from BitBake build outputs.
# The image can be flashed using Balena Etcher or dd.
#
# Must be run from inside the Docker Yocto container or on
# a Linux host with the build outputs accessible.
#
# Usage:
#   ./generate-sdcard-img.sh
#
# Output:
#   /home/edf/projects/EDF/sdcard.img
#   /home/edf/projects/EDF/sdcard.img.bmap (for bmaptool)
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
MACHINE_BOOT="zynqmp-zcu104-sdt-full"
MACHINE_LINUX="zynqmp-zcu104-sdt-full"
IMAGE_NAME="core-image-minimal"
OUTPUT_IMG="${OUTPUT_DIR}/sdcard.img"
OUTPUT_BMAP="${OUTPUT_DIR}/sdcard.img.bmap"

DEPLOY_BOOT="${BUILD_DIR}/tmp/deploy/images/${MACHINE_BOOT}"
DEPLOY_LINUX="${BUILD_DIR}/tmp/deploy/images/${MACHINE_LINUX}"

# ── Partition sizes ───────────────────────────────────────────
BOOT_SIZE_MB=512
# rootfs size determined by wic image

# ── Check required tools ──────────────────────────────────────
check_tools() {
    log_info "Checking required tools..."

    for tool in dd fdisk mkfs.vfat mcopy; do
        command -v "$tool" >/dev/null 2>&1 || \
            log_error "${tool} not found. Install with: sudo apt-get install -y dosfstools mtools"
    done

    command -v bmaptool >/dev/null 2>&1 && \
        HAVE_BMAPTOOL=1 || HAVE_BMAPTOOL=0

    log_success "Tools OK"
}

# ── Locate build outputs ──────────────────────────────────────
find_outputs() {
    log_info "Locating build outputs..."

    # Find WIC image
    WIC_IMAGE=$(ls "${DEPLOY_LINUX}/${IMAGE_NAME}"*.wic 2>/dev/null | head -1)
    BMAP_FILE=$(ls "${DEPLOY_LINUX}/${IMAGE_NAME}"*.wic.bmap 2>/dev/null | head -1)

    if [ -z "$WIC_IMAGE" ] || [ ! -f "$WIC_IMAGE" ]; then
        log_error "WIC image not found in ${DEPLOY_LINUX}/
        Run: MACHINE=${MACHINE_LINUX} bitbake ${IMAGE_NAME}"
    fi

    # Find ZCU104 boot files
    BOOT_BIN="${DEPLOY_BOOT}/BOOT.BIN"
    KERNEL="${DEPLOY_BOOT}/Image"
    DTB="${DEPLOY_BOOT}/system.dtb"
    BOOTSCR="${DEPLOY_BOOT}/boot.scr"

    [ -f "$BOOT_BIN" ] || \
        log_error "BOOT.BIN not found in ${DEPLOY_BOOT}/
        Run: MACHINE=${MACHINE_BOOT} bitbake xilinx-bootbin"

    [ -f "$KERNEL" ] || \
        log_error "Kernel Image not found in ${DEPLOY_BOOT}/"

    [ -f "$DTB" ] || \
        log_error "system.dtb not found in ${DEPLOY_BOOT}/"

    log_success "All build outputs found"
    echo ""
    log_info "  WIC image : ${WIC_IMAGE}"
    log_info "  BOOT.BIN  : ${BOOT_BIN} ($(du -sh ${BOOT_BIN} | cut -f1))"
    log_info "  Kernel    : ${KERNEL} ($(du -sh ${KERNEL} | cut -f1))"
    log_info "  DTB       : ${DTB} ($(du -sh ${DTB} | cut -f1))"
    if [ -f "$BOOTSCR" ]; then
        log_info "  boot.scr  : ${BOOTSCR}"
    fi
}

# ── Get WIC image size ────────────────────────────────────────
get_image_size() {
    WIC_SIZE_BYTES=$(stat -c%s "$WIC_IMAGE")
    WIC_SIZE_MB=$(( WIC_SIZE_BYTES / 1024 / 1024 + 1 ))
    log_info "WIC image size: ${WIC_SIZE_MB} MB"
}

# ── Build the SD card image ───────────────────────────────────
build_image() {
    log_info "Building SD card image..."
    echo ""

    WORK_DIR=$(mktemp -d)
    trap "rm -rf ${WORK_DIR}" EXIT

    # ── Step 1: Start with WIC image as base ──────────────────
    log_info "[1/4] Copying WIC image as base..."
    cp "${WIC_IMAGE}" "${OUTPUT_IMG}"
    log_success "Base image copied ($(du -sh ${OUTPUT_IMG} | cut -f1))"

    # ── Step 2: Find BOOT partition in image ──────────────────
    log_info "[2/4] Locating BOOT partition in image..."

    # Get partition info using fdisk
    PART_INFO=$(fdisk -l "${OUTPUT_IMG}" 2>/dev/null)
    echo "$PART_INFO"

    # Get start sector of first partition (FAT32 boot)
    BOOT_START=$(fdisk -l "${OUTPUT_IMG}" 2>/dev/null | \
        awk '/FAT|fat|W95|boot/{print $2; exit}')

    if [ -z "$BOOT_START" ]; then
        # Try getting first partition start sector
        BOOT_START=$(fdisk -l "${OUTPUT_IMG}" 2>/dev/null | \
            grep "^${OUTPUT_IMG}1" | awk '{print $2}')
    fi

    if [ -z "$BOOT_START" ]; then
        log_warn "Could not detect partition layout automatically"
        log_info "Trying offset 4096 sectors (2MB)"
        BOOT_START=4096
    fi

    BOOT_OFFSET=$(( BOOT_START * 512 ))
    log_info "BOOT partition starts at sector ${BOOT_START} (offset ${BOOT_OFFSET} bytes)"

    # ── Step 3: Mount BOOT partition and replace files ────────
    log_info "[3/4] Replacing boot files with ZCU104-specific versions..."

    MOUNT_POINT="${WORK_DIR}/boot"
    mkdir -p "${MOUNT_POINT}"

    # Mount the BOOT partition from the image file
    sudo mount -o loop,offset=${BOOT_OFFSET} "${OUTPUT_IMG}" "${MOUNT_POINT}" || {
        # Try using loopback device directly
        LOOP_DEV=$(sudo losetup -f --show -P "${OUTPUT_IMG}")
        sudo mount "${LOOP_DEV}p1" "${MOUNT_POINT}"
        USING_LOOP=1
    }

    log_info "Current BOOT partition contents:"
    ls -lh "${MOUNT_POINT}" | sed 's/^/    /'

    # Copy ZCU104-specific boot files
    sudo cp "${BOOT_BIN}"  "${MOUNT_POINT}/BOOT.BIN"
    sudo cp "${KERNEL}"    "${MOUNT_POINT}/Image"
    sudo cp "${DTB}"       "${MOUNT_POINT}/system.dtb"

    if [ -f "$BOOTSCR" ]; then
        sudo cp "${BOOTSCR}" "${MOUNT_POINT}/boot.scr"
    fi

    log_info "Updated BOOT partition contents:"
    ls -lh "${MOUNT_POINT}" | sed 's/^/    /'

    sudo umount "${MOUNT_POINT}"

    if [ -n "$USING_LOOP" ]; then
        sudo losetup -d "${LOOP_DEV}"
    fi

    log_success "Boot files replaced"

    # ── Step 4: Generate bmap file ────────────────────────────
    log_info "[4/4] Generating bmap file..."

    if [ "$HAVE_BMAPTOOL" = "1" ]; then
        bmaptool create "${OUTPUT_IMG}" > "${OUTPUT_BMAP}"
        log_success "bmap file generated: ${OUTPUT_BMAP}"
    else
        log_warn "bmaptool not found — skipping bmap generation"
        log_warn "Install with: sudo apt-get install -y bmap-tools"
    fi
}

# ── Print summary ─────────────────────────────────────────────
print_summary() {
    echo ""
    echo "============================================"
    echo " SD Card Image Ready"
    echo "============================================"
    echo ""
    log_success "Image   : ${OUTPUT_IMG}"
    echo "         Size: $(du -sh ${OUTPUT_IMG} | cut -f1)"

    if [ -f "$OUTPUT_BMAP" ]; then
        log_success "bmap    : ${OUTPUT_BMAP}"
    fi

    echo ""
    echo " Flash options:"
    echo ""
    echo " 1. Balena Etcher (GUI, any OS):"
    echo "    Open Etcher → Select ${OUTPUT_IMG}"
    echo "    → Select SD card → Flash"
    echo ""
    echo " 2. bmaptool (fast, Linux):"
    echo "    sudo bmaptool copy ${OUTPUT_IMG} /dev/sdX"
    echo ""
    echo " 3. dd (fallback, Linux):"
    echo "    sudo dd if=${OUTPUT_IMG} of=/dev/sdX \\"
    echo "        bs=4M status=progress conv=fsync"
    echo ""
    echo " ZCU104 boot mode switches:"
    echo "    SW6: 1=OFF 2=OFF 3=OFF 4=ON (SD card boot)"
    echo ""
    echo " Serial console: 115200 baud, /dev/ttyUSB1"
    echo "============================================"
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