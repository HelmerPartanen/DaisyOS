#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
profile="$repo_root/os/archiso/DaisyOS"

required_files=(
  profiledef.sh
  packages.x86_64
  pacman.conf
  efiboot/loader/loader.conf
  efiboot/loader/entries/01-daisyos-linux.conf
  syslinux/syslinux.cfg
  airootfs/etc/mkinitcpio.conf.d/archiso.conf
  airootfs/etc/mkinitcpio.d/linux.preset
  airootfs/etc/hostname
  airootfs/etc/locale.conf
  airootfs/etc/locale.gen
  airootfs/etc/vconsole.conf
  airootfs/etc/systemd/system/daisyos-setup.service
  airootfs/etc/systemd/system/getty@tty1.service.d/autologin.conf
  airootfs/usr/local/bin/daisyos-setup.sh
  airootfs/usr/local/bin/daisyos-start-session
  airootfs/usr/local/bin/daisyos-xorg-session
  airootfs/usr/local/bin/daisyos-session
  airootfs/usr/local/bin/daisyos-fallback-session
)

for file in "${required_files[@]}"; do
  if [[ ! -f "$profile/$file" ]]; then
    echo "Missing ISO profile file: os/archiso/DaisyOS/$file"
    exit 1
  fi
done

if ! grep -Eq '^bootmodes=\([^)]*"bios\.syslinux"[^)]*"uefi\.systemd-boot"' \
    "$profile/profiledef.sh"; then
  echo "The ISO profile must use the current BIOS and UEFI archiso boot modes."
  exit 1
fi

if grep -Eq '="(644|755):0:0"' "$profile/profiledef.sh"; then
  echo "ISO file permissions must use owner:group:mode order."
  exit 1
fi

if ! grep -Fq '["/opt/DaisyOS/shell/DaisyOS.Shell"]="0:0:755"' \
    "$profile/profiledef.sh"; then
  echo "The staged self-contained shell must remain executable inside the image."
  exit 1
fi

if ! grep -Fq '["/opt/DaisyOS/shell/createdump"]="0:0:755"' \
    "$profile/profiledef.sh"; then
  echo "The live shell crash-dump helper must be executable inside the image."
  exit 1
fi

if ! grep -Fq 'console=ttyS0,115200n8' \
    "$profile/efiboot/loader/entries/01-daisyos-linux.conf" \
    || ! grep -Fq 'console=ttyS0,115200n8' "$profile/syslinux/syslinux.cfg"; then
  echo "BIOS and UEFI boot entries must expose a serial console for VM diagnosis."
  exit 1
fi

if ! grep -Fxq 'timeout 0' "$profile/efiboot/loader/loader.conf" \
    || ! grep -Fxq 'MENU HIDDEN' "$profile/syslinux/syslinux.cfg"; then
  echo "The live image must auto-boot without presenting a routine boot chooser."
  exit 1
fi

if ! grep -Eq 'HOOKS=.*[[:space:]]archiso([[:space:]]|$)' \
    "$profile/airootfs/etc/mkinitcpio.conf.d/archiso.conf"; then
  echo "The live initramfs must include the archiso mount hook."
  exit 1
fi

if ! grep -Fq "archiso_config='/etc/mkinitcpio.conf.d/archiso.conf'" \
    "$profile/airootfs/etc/mkinitcpio.d/linux.preset"; then
  echo "The kernel preset must build with the live-image initramfs configuration."
  exit 1
fi

if grep -Eq 'archiso_pxe_' \
    "$profile/airootfs/etc/mkinitcpio.conf.d/archiso.conf"; then
  echo "The local-only live image must not request unavailable PXE initramfs helpers."
  exit 1
fi

if [[ ! -s "$profile/airootfs/etc/hostname" || ! -s "$profile/airootfs/etc/locale.conf" ]]; then
  echo "The live image needs deterministic hostname and locale defaults."
  exit 1
fi

if ! grep -Fq 'ln -sfn /usr/share/zoneinfo/UTC /etc/localtime' \
    "$profile/airootfs/usr/local/bin/daisyos-setup.sh"; then
  echo "The early live-media setup must provide a noninteractive UTC default."
  exit 1
fi

if ! grep -Fq 'locale-gen' "$profile/airootfs/usr/local/bin/daisyos-setup.sh"; then
  echo "The live-media setup must generate its configured locale."
  exit 1
fi

if ! grep -Eq 'useradd .*-[^ ]*G [^ ]*tty' \
    "$profile/airootfs/usr/local/bin/daisyos-setup.sh"; then
  echo "The disposable live user must be able to write serial-only boot evidence."
  exit 1
fi

if ! grep -Fq 'Before=systemd-firstboot.service' \
    "$profile/airootfs/etc/systemd/system/daisyos-setup.service"; then
  echo "Live defaults must be applied before the interactive first-boot service."
  exit 1
fi

packages="$(sed '/^[[:space:]]*#/d; /^[[:space:]]*$/d' "$profile/packages.x86_64")"
duplicates="$(printf '%s\n' "$packages" | sort | uniq -d)"
if [[ -n "$duplicates" ]]; then
  echo "Duplicate packages in the ISO profile:"
  printf '%s\n' "$duplicates"
  exit 1
fi

required_packages=(
  base
  linux
  linux-firmware
  mkinitcpio
  mkinitcpio-archiso
  syslinux
  networkmanager
  dbus
  pipewire
  pipewire-pulse
  wireplumber
  mesa
  xorg-server
  xorg-xinit
  xorg-xwayland
  kwin
  plasma-workspace
  polkit-kde-agent
  xdg-desktop-portal
  xdg-desktop-portal-kde
  xterm
  qemu-guest-agent
)
for package in "${required_packages[@]}"; do
  if ! grep -Fxq "$package" <<<"$packages"; then
    echo "Required live ISO package is missing: $package"
    exit 1
  fi
done

if ! grep -Fq -- '--autologin daisyos' \
    "$profile/airootfs/etc/systemd/system/getty@tty1.service.d/autologin.conf"; then
  echo "The live ISO must explicitly auto-login its disposable daisyos account."
  exit 1
fi

if ! grep -Fq 'exec /usr/local/bin/daisyos-start-session' \
    "$profile/airootfs/etc/skel/.bash_profile"; then
  echo "The live console must start through the compositor-aware session runner."
  exit 1
fi

if ! grep -Fq 'exec /usr/local/bin/daisyos-xorg-session' \
    "$profile/airootfs/etc/skel/.xinitrc"; then
  echo "The live X session must retain the compositor-managed compatibility path."
  exit 1
fi

if ! grep -Fq 'DAISYOS_BOOT_EVIDENCE_CONSOLE:-/dev/ttyS0' \
    "$profile/airootfs/usr/local/bin/daisyos-start-session"; then
  echo "The live session must expose a serial-only startup marker for VM verification."
  exit 1
fi

if ! grep -Fq -- '--drm' "$profile/airootfs/usr/local/bin/daisyos-start-session" \
    || ! grep -Fq -- '--xwayland' "$profile/airootfs/usr/local/bin/daisyos-start-session" \
    || ! grep -Fq 'exec startx' "$profile/airootfs/usr/local/bin/daisyos-start-session"; then
  echo "The primary KWin Wayland session must retain its XWayland and Xorg recovery contracts."
  exit 1
fi

if grep -Fq 'DAISYOS_SOFTWARE_RENDERING=1' \
    "$profile/airootfs/etc/skel/.xinitrc"; then
  echo "The live image must not force Avalonia's unstable X11 software renderer."
  exit 1
fi

shell_view="$repo_root/src/DaisyOS.Shell/Views/ShellView.axaml"
if grep -Fq 'ShellRenderHost' "$shell_view"; then
  echo "The live desktop must not place the obsolete GPU vibrancy surface over shell content."
  exit 1
fi

smoke_test="$repo_root/scripts/smoke-test-iso.sh"
if ! grep -Fq 'nonblack >= 0.20' "$smoke_test" \
    || ! grep -Fq 'no longer fully visible after the stability window' "$smoke_test"; then
  echo "The ISO smoke gate must reject partial frames before and after stability."
  exit 1
fi

for audio_service in pipewire.service pipewire-pulse.service wireplumber.service; do
  if ! grep -Fq "$audio_service" "$profile/airootfs/usr/local/bin/daisyos-start-session"; then
    echo "The live session must start $audio_service before the shell."
    exit 1
  fi
done

if [[ ! -L "$profile/airootfs/etc/systemd/system/multi-user.target.wants/daisyos-setup.service" ]]; then
  echo "The live-user setup service is not enabled in the ISO profile."
  exit 1
fi

guest_agent_rule="$profile/airootfs/etc/udev/rules.d/99-daisyos-qemu-guest-agent.rules"
if [[ ! -f "$guest_agent_rule" ]] \
    || ! grep -Fq 'org.qemu.guest_agent.0' "$guest_agent_rule" \
    || ! grep -Fq 'SYSTEMD_WANTS}+="qemu-guest-agent.service"' "$guest_agent_rule"; then
  echo "QEMU diagnostics must be triggered only when the guest-agent channel exists."
  exit 1
fi

if command -v shellcheck >/dev/null 2>&1; then
  shellcheck \
    "$profile/profiledef.sh" \
    "$profile/airootfs/usr/local/bin/DaisyOS-start-shell" \
    "$profile/airootfs/usr/local/bin/daisyos-setup.sh" \
    "$profile/airootfs/usr/local/bin/daisyos-start-session" \
    "$profile/airootfs/usr/local/bin/daisyos-xorg-session" \
    "$profile/airootfs/usr/local/bin/daisyos-session" \
    "$profile/airootfs/usr/local/bin/daisyos-fallback-session" \
    "$profile/airootfs/usr/local/bin/daisyos-recover-session"
fi

echo "ISO profile checks passed."
