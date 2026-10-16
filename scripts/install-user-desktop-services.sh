#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
[[ $# -eq 0 ]] || { echo 'Usage: install-user-desktop-services.sh' >&2; exit 2; }
binary="$repo_root/src/DaisyOS.Shell/bin/Debug/net10.0/DaisyOS.Shell"
for helper in DaisyOS.Shell daisyos-polkit-agent daisyos-portal-capture daisy-screenshot-helper daisyos-portal-shortcuts; do
  [[ -x "$(dirname "$binary")/$helper" ]] || { echo "Missing $helper. Run scripts/build-desktop-services.sh and build the shell first." >&2; exit 1; }
done
config_dir="${XDG_CONFIG_HOME:-$HOME/.config}"
data_dir="${XDG_DATA_HOME:-$HOME/.local/share}"
staging="$(mktemp -d)"
trap 'rm -rf -- "$staging"' EXIT
"$repo_root/scripts/stage-desktop-services.sh" --root "$staging" --capture-helper "$(dirname "$binary")/daisyos-portal-capture"
install -Dm755 "$repo_root/scripts/daisyos-portal-frontend" "$HOME/.local/bin/daisyos-portal-frontend"
install -Dm755 "$repo_root/scripts/daisyos-desktop-services" "$HOME/.local/bin/daisyos-desktop-services"
install -Dm644 "$staging/usr/share/xdg-desktop-portal/portals/daisyos.portal" "$data_dir/xdg-desktop-portal/portals/daisyos.portal"
install -Dm644 "$repo_root/packaging/xdg-desktop-portal/daisyos-portals.conf" "$config_dir/xdg-desktop-portal/daisyos-portals.conf"
install -Dm644 "$repo_root/packaging/xdg-desktop-portal/daisyos-portals.conf" "$data_dir/xdg-desktop-portal/portals/daisyos-portals.conf"
# Retain installed non-UI providers (notably existing application secrets).
for portal in /usr/share/xdg-desktop-portal/portals/*.portal; do
  [[ "$(basename "$portal")" == daisyos.portal ]] && continue
  ln -sfn "$portal" "$data_dir/xdg-desktop-portal/portals/$(basename "$portal")"
done
for name in org.daisyos.PortalCapture org.daisyos.screenshot-helper; do
  install -Dm644 "$staging/usr/share/applications/$name.desktop" "$data_dir/applications/$name.desktop"
done
install -d "$config_dir/DaisyOS" "$config_dir/systemd/user" "$data_dir/dbus-1/services"
printf 'DAISYOS_DESKTOP_SERVICES_BINARY=%q\n' "$binary" > "$config_dir/DaisyOS/desktop-services.env"
# Paths are encoded for their file formats, never interpolated into shell commands.
python - "$repo_root" "$HOME/.local/bin/daisyos-desktop-services" "$config_dir" "$data_dir" <<'PY'
import pathlib,sys
repo,wrapper,config,data=map(pathlib.Path,sys.argv[1:])
# systemd specifiers and desktop-entry percent codes are escaped independently.
unit=(repo/'packaging/xdg-desktop-portal/daisyos-desktop-services.service').read_text()
systemd=str(wrapper).replace('\\','\\\\').replace('"','\\"').replace('%','%%')
(config/'systemd/user/daisyos-desktop-services.service').write_text(unit.replace('/usr/local/bin/daisyos-desktop-services','"'+systemd+'"'))
frontend=str(wrapper.with_name('daisyos-portal-frontend')).replace('\\','\\\\').replace('"','\\"').replace('%','%%')
override=config/'systemd/user/xdg-desktop-portal.service.d'
override.mkdir(parents=True, exist_ok=True)
(override/'50-daisyos-frontend.conf').write_text('[Service]\nExecStart=\nExecStart="'+frontend+'"\n')
service=(repo/'packaging/xdg-desktop-portal/org.freedesktop.impl.portal.desktop.daisyos.service').read_text()
exec_path=str(wrapper).replace('\\','\\\\').replace('"','\\"').replace('%','%%')
(data/'dbus-1/services/org.freedesktop.impl.portal.desktop.daisyos.service').write_text(service.replace('/usr/local/bin/daisyos-desktop-services','"'+exec_path+'"'))
PY
systemctl --user daemon-reload
kbuildsycoca6 --noincremental
printf '%s\n' 'Installed DaisyOS desktop services for this user. Run scripts/prepare-screen-sharing.sh in the DaisyOS session to activate them.'
