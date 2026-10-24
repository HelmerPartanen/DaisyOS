#!/usr/bin/env bash
set -Eeuo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
prefix="/"
source_root="$repo_root"

usage() {
  cat <<'EOF'
Usage: scripts/install-development-session.sh [--repo PATH] [--prefix PATH]

Installs the DaisyOS (Windowed Prototype) Wayland session entry. Select it from the
display manager after signing out. --prefix is intended for tests or staging;
the normal install target is /.
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
launcher_target="$bin_dir/daisyos-development-session"
entry_target="$session_dir/daisyos-development.desktop"
config_target="$config_dir/daisyos-development-session.conf"

install -d -m 0755 "$bin_dir" "$session_dir" "$config_dir"
install -m 0755 "$repo_root/scripts/daisyos-development-session" "$launcher_target"
install -m 0644 "$repo_root/packaging/wayland-sessions/daisyos-development.desktop" "$entry_target"
install -d -m 0755 "$prefix/usr/share/xdg-desktop-portal"
install -m 0644 "$repo_root/packaging/xdg-desktop-portal/daisyos-portals.conf" \
  "$prefix/usr/share/xdg-desktop-portal/daisyos-portals.conf"
install -D -m 0644 "$repo_root/packaging/kde/kwalletrc" "$prefix/etc/xdg/kwalletrc"
"$repo_root/scripts/stage-desktop-services.sh" --root "$prefix" \
  --capture-helper "$source_root/src/DaisyOS.Shell/bin/Debug/net10.0/daisyos-portal-capture"

# Preserve the selected checkout when refreshing the launcher, unless an
# explicit --repo path was supplied for this installation.
if [[ ! -f "$config_target" || "$source_root" != "$repo_root" ]]; then
  printf 'DAISYOS_DEVELOPMENT_ROOT=%q\n' "$source_root" > "$config_target"
  chmod 0644 "$config_target"
fi

if [[ -d "$repo_root/os/archiso/DaisyOS/airootfs/usr/share/icons/default" ]]; then
  install -d -m 0755 "$prefix/usr/share/icons"
  cp -a "$repo_root/os/archiso/DaisyOS/airootfs/usr/share/icons/default" "$prefix/usr/share/icons/"
fi

echo "Installed DaisyOS (Windowed Prototype). Sign out, then select it from the display-manager session menu."
