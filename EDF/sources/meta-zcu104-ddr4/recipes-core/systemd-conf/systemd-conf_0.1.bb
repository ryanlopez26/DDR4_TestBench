SUMMARY = "Static network config for ZCU104"
LICENSE = "CLOSED"

SRC_URI = "file://eth0-static.network"

S = "${WORKDIR}"

inherit systemd
SYSTEMD_AUTO_ENABLE = "enable"
SYSTEMD_SERVICE:${PN} = "systemd-networkd.service"

FILES:${PN} += "${sysconfdir}/systemd/network/eth0-static.network"

do_install() {
    install -d ${D}${sysconfdir}/systemd/network
    install -m 0644 ${WORKDIR}/eth0-static.network \
        ${D}${sysconfdir}/systemd/network/
}