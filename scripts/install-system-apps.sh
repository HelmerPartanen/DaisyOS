#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
app_configuration="${DAISYOS_APP_CONFIGURATION:-Release}"
[[ "$app_configuration" == Release || "$app_configuration" == Debug ]] || { echo "DAISYOS_APP_CONFIGURATION must be Release or Debug." >&2; exit 1; }
"$repo_root/scripts/install-files-app.sh" "${1:-$repo_root/src/DaisyOS.Files/bin/$app_configuration/net10.0/DaisyOS.Files}"
data_home="${XDG_DATA_HOME:-$HOME/.local/share}"
for application in Notes Settings DiskManager; do
  binary="$repo_root/src/DaisyOS.$application/bin/$app_configuration/net10.0/DaisyOS.$application"
  if [[ $# -gt 0 ]]; then binary="$(dirname "$1")/../$application/DaisyOS.$application"; fi
  [[ -x "$binary" ]] || { echo "Build DaisyOS.$application before installing it: $binary" >&2; exit 1; }
  binary="$(realpath "$binary")"
  lower="${application,,}"
  icon_name="$application"
  [[ "$application" != DiskManager ]] || icon_name=Disk_Manager
  install -Dm0644 "$repo_root/src/DaisyOS.Shell/Assets/AppIcons/${icon_name}_Dark.png" "$data_home/icons/hicolor/1024x1024/apps/org.daisyos.$lower.png"
  ln -sfn "$binary" "$HOME/.local/bin/daisyos-$lower"
  python3 - "$repo_root/src/native/daisy_window_decoration/applications/org.daisyos.$lower.desktop" "$data_home/applications/org.daisyos.$lower.desktop" "$binary" "$lower" <<'PY'
import sys
from pathlib import Path
source, target, binary, name = sys.argv[1:]
quoted = binary.replace('\\', '\\\\').replace('"', '\\"').replace('`', '\\`').replace('$', '\\$').replace('\\', '\\\\')
Path(target).write_text(Path(source).read_text().replace('Exec=daisyos-' + name, 'Exec="' + quoted + '"'))
PY
  echo "Installed standalone $application: $binary"
done
python3 "$repo_root/scripts/update-system-app-shortcuts.py" "$repo_root" "$app_configuration"
command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "$data_home/applications" || true
