#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
binary="${1:-$repo_root/src/DaisyOS.Files/bin/${DAISYOS_APP_CONFIGURATION:-Release}/net10.0/DaisyOS.Files}"
[[ -x "$binary" ]] || { echo "Build DaisyOS.Files before installing it: $binary" >&2; exit 1; }
binary="$(realpath "$binary")"
data_home="${XDG_DATA_HOME:-$HOME/.local/share}"
mkdir -p "$HOME/.local/bin" "$data_home/applications" "$data_home/dbus-1/services"
ln -sfn "$binary" "$HOME/.local/bin/daisyos-files"
# Absolute Exec works even when the session PATH omits ~/.local/bin.
python3 - "$repo_root/src/native/daisy_window_decoration/applications/org.daisyos.files.desktop" "$data_home/applications/org.daisyos.files.desktop" "$binary" <<'PY'
import sys
from pathlib import Path
source, target, binary = sys.argv[1:]
# Desktop Exec quoting has two layers: desktop-string escaping, then argument escaping.
quoted = binary.replace('\\', '\\\\').replace('"', '\\"').replace('`', '\\`').replace('$', '\\$')
quoted = quoted.replace('\\', '\\\\')
text = Path(source).read_text().replace('Exec=daisyos-files %U', 'Exec="' + quoted + '" %U')
Path(target).write_text(text)
PY
service_file="$data_home/dbus-1/services/org.freedesktop.FileManager1.service"
if [[ ! -f "$service_file" ]] || rg -q 'DaisyOS.Files|daisyos-files' "$service_file"; then
  python3 - "$binary" "$service_file" <<'PY_SERVICE'
import shlex, sys
from pathlib import Path
Path(sys.argv[2]).write_text('[D-BUS Service]\nName=org.freedesktop.FileManager1\nExec=' + shlex.quote(sys.argv[1]) + '\n')
PY_SERVICE
else
  echo "Kept the existing file-manager D-Bus activation configuration: $service_file"
fi
command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "$data_home/applications" || true
# Refresh activation metadata in the active user bus after an actual installation.
if [[ "$data_home" == "${XDG_DATA_HOME:-$HOME/.local/share}" && -n "${DBUS_SESSION_BUS_ADDRESS:-}" ]] && command -v busctl >/dev/null 2>&1; then
  busctl --user call org.freedesktop.DBus /org/freedesktop/DBus org.freedesktop.DBus ReloadConfig || true
fi
echo "Installed standalone Files: $binary"
