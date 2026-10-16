#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
[[ "${1:-}" == --root && $# -ge 2 ]] || { echo 'Usage: stage-desktop-services.sh --root PATH [--capture-helper PATH]' >&2; exit 2; }
stage_root="$2"; shift 2
capture_helper=/opt/DaisyOS/shell/daisyos-portal-capture
if [[ "${1:-}" == --capture-helper && $# -eq 2 ]]; then capture_helper="$2"; shift 2; fi
[[ $# -eq 0 && "$capture_helper" == /* && "$capture_helper" != *$'\n'* ]] || exit 2
assets="$repo_root/packaging/xdg-desktop-portal"
install -Dm755 "$repo_root/scripts/daisyos-desktop-services" "$stage_root/usr/local/bin/daisyos-desktop-services"
install -Dm644 "$assets/daisyos.portal" "$stage_root/usr/share/xdg-desktop-portal/portals/daisyos.portal"
install -Dm644 "$assets/org.freedesktop.impl.portal.desktop.daisyos.service" "$stage_root/usr/share/dbus-1/services/org.freedesktop.impl.portal.desktop.daisyos.service"
install -Dm644 "$assets/daisyos-desktop-services.service" "$stage_root/usr/lib/systemd/user/daisyos-desktop-services.service"
install -Dm644 "$assets/org.daisyos.PortalCapture.desktop" "$stage_root/usr/share/applications/org.daisyos.PortalCapture.desktop"
python - "$stage_root/usr/share/applications/org.daisyos.PortalCapture.desktop" "$capture_helper" <<'PY'
import pathlib,sys
path=pathlib.Path(sys.argv[1])
# Desktop-entry Exec quoting is not shell quoting.
value=sys.argv[2].replace('\\','\\\\\\\\').replace('"','\\"').replace('`','\\`').replace('$','\\$').replace('%','%%')
path.write_text(path.read_text().replace('Exec=/opt/DaisyOS/shell/daisyos-portal-capture','Exec="'+value+'"'))
PY

install -Dm644 "$repo_root/src/native/daisy_screenshot/org.daisyos.screenshot-helper.desktop" "$stage_root/usr/share/applications/org.daisyos.screenshot-helper.desktop"
python - "$stage_root/usr/share/applications/org.daisyos.screenshot-helper.desktop" "$capture_helper" <<'PYSH'
import pathlib,sys
path=pathlib.Path(sys.argv[1])
helper=str(pathlib.Path(sys.argv[2]).with_name('daisy-screenshot-helper'))
value=helper.replace('\\','\\\\\\\\').replace('"','\\"').replace('`','\\`').replace('$','\\$').replace('%','%%')
path.write_text('\n'.join('Exec="'+value+'"' if line.startswith('Exec=') else line for line in path.read_text().splitlines())+'\n')
PYSH
