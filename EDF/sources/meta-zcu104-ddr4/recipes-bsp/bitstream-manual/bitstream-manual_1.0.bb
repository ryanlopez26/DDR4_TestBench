SUMMARY = "Manually-supplied PL bitstream for ZCU104 (bypasses sdt-artifacts)"
DESCRIPTION = "Takes a System_wrapper.bit dropped in files/ and installs it \
where xilinx-bootbin expects to find the PL bitstream. Provides virtual/bitstream \
in place of the upstream bitstream_1.0.bb recipe."
LICENSE = "CLOSED"

PROVIDES = "virtual/bitstream"

SRC_URI = "file://System_wrapper.bit"
S = "${WORKDIR}"

# This recipe has no sources to compile or configure — it's just a file-copy.
INHIBIT_DEFAULT_DEPS = "1"
DEPENDS = ""
do_compile[noexec] = "1"
do_configure[noexec] = "1"

# xilinx-bootbin reads BIF_PARTITION_IMAGE[bitstream] which (in meta-xilinx-core
# 1.0) resolves through the sysroot at /usr/share/sdt/${MACHINE}/<name>.bit.
# Install our file there with the name the rest of the recipe chain expects.
do_install() {
    install -d ${D}/usr/share/sdt/${MACHINE}
    install -m 0644 ${WORKDIR}/System_wrapper.bit \
                    ${D}/usr/share/sdt/${MACHINE}/System_wrapper.bit
}

FILES:${PN} = "/usr/share/sdt/${MACHINE}/System_wrapper.bit"

# Make the file available in dependent recipes' sysroots (this is the step
# that was missing — the upstream recipe wasn't populating into the sysroot).
SYSROOT_DIRS += "/usr/share/sdt"

# Also deploy a copy for inspection / SD-image fallback paths.
do_deploy() {
    install -d ${DEPLOYDIR}
    install -m 0644 ${WORKDIR}/System_wrapper.bit ${DEPLOYDIR}/System_wrapper.bit
}
addtask deploy after do_install before do_build

# Skip QA checks that don't apply to a prebuilt binary blob.
INSANE_SKIP:${PN} += "already-stripped arch"
