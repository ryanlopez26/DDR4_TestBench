# Copy built server executable into the needed bblayer
cp -f zcu_app/zcu_server EDF/sources/meta-zcu104-ddr4/recipes-apps/zcu-server/files/

# Build the image
MACHINE=zynqmp-zcu104-sdt-full bitbake core-image-minimal