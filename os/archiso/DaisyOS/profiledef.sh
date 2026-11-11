#!/usr/bin/env bash
# shellcheck disable=SC2034

iso_name="DaisyOS"
iso_label="DAISYOS_$(date +%Y%m)"
iso_publisher="DaisyOS"
iso_application="DaisyOS Live ISO"
iso_version="$(date +%Y.%m.%d)"
install_dir="arch"
buildmodes=("iso")
bootmodes=("bios.syslinux" "uefi.systemd-boot")
arch="x86_64"
pacman_conf="pacman.conf"
airootfs_image_type="squashfs"
airootfs_image_tool_options=("-comp" "zstd" "-Xcompression-level" "15" "-b" "1M")
file_permissions=(
  ["/usr/local/bin/daisyos-game"]="0:0:755"
  ["/usr/local/bin/daisyos-gaming-diagnostics"]="0:0:755"
  ["/usr/local/bin/daisyos-game-launcher"]="0:0:755"
  ["/usr/local/bin/steam"]="0:0:755"
  ["/usr/local/bin/lutris"]="0:0:755"
  ["/usr/local/bin/wine"]="0:0:755"
  ["/usr/local/bin/wine64"]="0:0:755"
  ["/usr/local/lib/daisyos/gaming-environment"]="0:0:644"
  ["/usr/local/lib/daisyos/daisyos-memory-health"]="0:0:755"
  ["/usr/share/polkit-1/rules.d/60-daisyos-gamemode.rules"]="0:0:644"
  ["/opt/DaisyOS/shell/DaisyOS.Shell"]="0:0:755"
  ["/opt/DaisyOS/shell/createdump"]="0:0:755"
  ["/usr/local/bin/DaisyOS-start-shell"]="0:0:755"
  ["/usr/local/bin/daisyos-start-session"]="0:0:755"
  ["/usr/local/bin/daisyos-xorg-session"]="0:0:755"
  ["/usr/local/bin/daisyos-setup.sh"]="0:0:755"
  ["/usr/local/bin/daisyos-session"]="0:0:755"
  ["/usr/local/bin/daisyos-fallback-session"]="0:0:755"
  ["/usr/local/bin/daisyos-recover-session"]="0:0:755"
  ["/etc/skel/.bash_profile"]="0:0:644"
  ["/etc/skel/.xinitrc"]="0:0:755"
)
