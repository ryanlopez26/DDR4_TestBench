SUMMARY = "ZCU104 DDR4 test server"
LICENSE = "CLOSED"

SRC_URI = "file://zcu-server \
           file://zcu-server.service"

S = "${WORKDIR}"

inherit systemd

SYSTEMD_AUTO_ENABLE = "enable"
SYSTEMD_SERVICE:${PN} = "zcu-server.service"

FILES:${PN} += "${bindir}/zcu-server ${systemd_unitdir}/system/zcu-server.service"

# Skip QA checks that don't apply to a prebuilt binary
INSANE_SKIP:${PN} += "already-stripped ldflags"

do_install() {
    install -d ${D}${bindir}
    install -m 0755 ${WORKDIR}/zcu-server ${D}${bindir}/zcu-server

    install -d ${D}${systemd_unitdir}/system
    install -m 0644 ${WORKDIR}/zcu-server.service ${D}${systemd_unitdir}/system/
}