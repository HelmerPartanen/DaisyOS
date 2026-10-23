#!/usr/bin/env bash
set -Eeuo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
prefix="/"
source_root="$repo_root"

usage() {
  cat <<'EOF'
Usage: scripts/install-native-development-session.sh [--repo PATH] [--prefix PATH]

Installs the DaisyOS (Native Development) Wayland session entry. It is a real
layer-shell development session, separate from the safe Windowed Prototype.
--prefix is intended for tests or staging; the normal target is /.
EOF
}

while (($# > 0)); do
  case "$1" in
    --repo)
      [[ $# -ge 2 ]] || { echo "--repo requires a path." >&2; exit 2; }
      source_root="$2"
      shift 2
      ;;
    --prefix)
      [[ $# -ge 2 ]] || { echo "--prefix requires a path." >&2; exit 2; }
      prefix="$2"
      shift 2
      ;;
    --help|-h)
      usage
      exit 0
      ;;
    *)
      echo "Unknown option: $1" >&2
      usage >&2
      exit 2
      ;;
  esac
done

if [[ "$prefix" == "/" && "$EUID" -ne 0 ]]; then
  echo "Administrator privileges are required. Run: sudo $0" >&2
  exit 1
fi

if [[ ! -d "$source_root/src/DaisyOS.Shell" ]]; then
  echo "Not a DaisyOS checkout: $source_root" >&2
  exit 1
fi

source_root="$(cd "$source_root" && pwd -P)"
bin_dir="$prefix/usr/local/bin"
session_dir="$prefix/usr/share/wayland-sessions"
config_dir="$prefix/etc/daisyos"
session_target="$bin_dir/daisyos-native-development-session"
control_target="$bin_dir/daisyos-native-development"
entry_target="$session_dir/daisyos-native-development.desktop"
config_target="$config_dir/daisyos-native-development.conf"

configure_localsend_firewall() {
  # LocalSend needs the same port for authenticated HTTPS transfers and LAN
  # discovery. Keep it reachable on the active LAN zone so enabling receiving
  # remains instant and never asks the user to authorize a firewall change.
  if ! command -v firewall-cmd >/dev/null 2>&1; then
    echo "Note: firewalld is not installed; no LocalSend firewall rule was needed."
    return 0
  fi

  if ! firewall-cmd --state >/dev/null 2>&1; then
    echo "Note: firewalld is not running; no LocalSend firewall rule was needed."
    return 0
  fi

  # Open the port on the zone that owns the primary LAN route, rather than
  # blindly changing the configured default zone. They often differ on laptops
  # with Wi-Fi, Ethernet, and VPN connections.
  local interface zone
  interface="$(ip -o route show default 2>/dev/null | awk 'NR == 1 { for (field = 1; field <= NF; field++) if ($field == "dev") { print $(field + 1); exit } }')"
  zone=""
  if [[ -n "$interface" ]]; then
    zone="$(firewall-cmd --get-zone-of-interface="$interface" 2>/dev/null || true)"
  fi
  zone="${zone:-$(firewall-cmd --get-default-zone)}"
  if ! firewall-cmd --permanent --zone="$zone" --add-port=53317/tcp >/dev/null \
    || ! firewall-cmd --permanent --zone="$zone" --add-port=53317/udp >/dev/null \
    || ! firewall-cmd --zone="$zone" --add-port=53317/tcp >/dev/null \
    || ! firewall-cmd --zone="$zone" --add-port=53317/udp >/dev/null; then
    echo "Warning: could not open TCP/UDP port 53317 in firewalld for LocalSend. Incoming LocalSend transfers may be blocked." >&2
    return 0
  fi

  echo "Opened firewalld TCP/UDP port 53317 for LocalSend on the $zone zone."
}

install_file_atomically() {
  local mode="$1" source="$2" target="$3" staged
  if [[ ! -s "$source" ]]; then
    echo "Refusing to install an empty or unreadable file: $source" >&2
    return 1
  fi

  staged="$(mktemp "${target}.tmp.XXXXXX")"
  if ! install -m "$mode" "$source" "$staged" || [[ ! -s "$staged" ]]; then
    rm -f -- "$staged"
    echo "Failed to stage a non-empty replacement for $target; the installed file was left untouched." >&2
    return 1
  fi

  mv -f -- "$staged" "$target"
}

install -d -m 0755 "$bin_dir" "$session_dir" "$config_dir"
"$repo_root/scripts/stage-gaming-integration.sh" --root "$prefix"
install_file_atomically 0755 "$repo_root/scripts/daisyos-native-development-session" "$session_target"
install_file_atomically 0755 "$repo_root/scripts/daisyos-native-development" "$control_target"
install_file_atomically 0644 "$repo_root/packaging/wayland-sessions/daisyos-native-development.desktop" "$entry_target"
install -d -m 0755 "$prefix/usr/share/xdg-desktop-portal"
install_file_atomically 0644 "$repo_root/packaging/xdg-desktop-portal/daisyos-portals.conf" \
  "$prefix/usr/share/xdg-desktop-portal/daisyos-portals.conf"
"$repo_root/scripts/stage-desktop-services.sh" --root "$prefix" \
  --capture-helper "$source_root/src/DaisyOS.Shell/bin/${DAISYOS_SHELL_CONFIGURATION:-Release}/net10.0/daisyos-portal-capture"

# Preserve an explicitly selected checkout when the session launcher is
# refreshed, unless the installation itself supplies a different checkout.
if [[ ! -f "$config_target" || "$source_root" != "$repo_root" ]]; then
  printf 'DAISYOS_NATIVE_DEVELOPMENT_ROOT=%q\n' "$source_root" > "$config_target"
  chmod 0644 "$config_target"
fi

if [[ -d "$repo_root/os/archiso/DaisyOS/airootfs/usr/share/icons/default" ]]; then
  install -d -m 0755 "$prefix/usr/share/icons"
  cp -a "$repo_root/os/archiso/DaisyOS/airootfs/usr/share/icons/default" "$prefix/usr/share/icons/"
fi

# systemd-oomd only watches cgroups explicitly marked by DaisyOS, so enabling
# it here adds early recovery for the native app slice without changing how
# unrelated desktops are managed. This is intentionally limited to a real
# system install; staging tests must never touch the host service manager.
if [[ "$prefix" == "/" ]]; then
  configure_localsend_firewall

  if systemctl list-unit-files bluetooth.service --no-legend >/dev/null 2>&1; then
    if ! systemctl enable --now bluetooth.service; then
      echo "Warning: bluetooth.service could not be enabled." >&2
    fi
  fi

  # Ensure Bluetooth / AirPods companion runtime and services are installed
  if command -v pacman >/dev/null 2>&1; then
    for pkg in qt6-connectivity bluez bluez-utils wireplumber; do
      if ! pacman -Q "$pkg" >/dev/null 2>&1; then
        pacman -S --needed --noconfirm "$pkg" >/dev/null 2>&1 || true
      fi
    done
  fi
  "$repo_root/scripts/build-librepods.sh" >/dev/null 2>&1 || true
  if [[ -x "$repo_root/artifacts/librepods/librepods" ]]; then
    "$repo_root/scripts/stage-airpods.sh" --root "$prefix" >/dev/null 2>&1 || true
  fi

  if systemctl list-unit-files systemd-oomd.service --no-legend >/dev/null 2>&1; then
    if ! systemctl enable --now systemd-oomd.service; then
      echo "Warning: systemd-oomd could not be enabled. DaisyOS will still throttle apps at its memory boundary, but early OOM recovery is unavailable." >&2
    fi
  else
    echo "Warning: systemd-oomd is not installed. DaisyOS will still throttle apps at its memory boundary, but early OOM recovery is unavailable." >&2
  fi
fi

echo "Installed DaisyOS (Native Development). Install the native KWin titlebar with: sudo $source_root/scripts/install-kwin-window-decoration.sh"
echo "For rounded third-party client-side windows, run as the desktop user: $source_root/scripts/install-kwin-frame-effect.sh"
echo "Sign out, select it from the display-manager session menu, then use daisyos-native-development restart after code changes."
