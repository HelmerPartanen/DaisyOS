#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
binary="${1:-$repo_root/src/DaisyOS.Mail/bin/${DAISYOS_APP_CONFIGURATION:-Release}/net10.0/DaisyOS.Mail}"
if [[ $# -gt 0 ]]; then shift; fi
pin=false
if [[ ${1:-} == --pin ]]; then pin=true; shift; fi
[[ $# -eq 0 ]] || { echo "Usage: install-mail-app.sh [binary] [--pin]" >&2; exit 2; }
[[ -x "$binary" ]] || { echo "Build DaisyOS.Mail before installing it: $binary" >&2; exit 1; }
binary="$(realpath "$binary")"
data_home="${XDG_DATA_HOME:-$HOME/.local/share}"
mkdir -p "$HOME/.local/bin" "$data_home/applications"
ln -sfn "$binary" "$HOME/.local/bin/daisyos-mail"
python3 - "$repo_root/src/native/daisy_window_decoration/applications/org.daisyos.mail.desktop" "$data_home/applications/org.daisyos.mail.desktop" "$binary" <<'PY'
import sys
from pathlib import Path
source, target, binary = sys.argv[1:]
quoted = binary.replace('\\', '\\\\').replace('"', '\\"').replace('`', '\\`').replace('$', '\\$').replace('\\', '\\\\')
temporary = Path(target).with_suffix('.desktop.tmp')
temporary.write_text(Path(source).read_text().replace('Exec=daisyos-mail %u', 'Exec="' + quoted + '" %u'))
temporary.replace(target)
PY
for size in 256 1024; do
  install -Dm0644 "$repo_root/src/DaisyOS.Shell/Assets/AppIcons/Mail.png" "$data_home/icons/hicolor/${size}x${size}/apps/org.daisyos.mail.png"
done
if command -v update-desktop-database >/dev/null 2>&1; then update-desktop-database "$data_home/applications"; fi
if command -v xdg-mime >/dev/null 2>&1; then
  current="$(xdg-mime query default x-scheme-handler/mailto || true)"
  if [[ -z "$current" || "$current" == org.daisyos.mail.desktop ]]; then xdg-mime default org.daisyos.mail.desktop x-scheme-handler/mailto; fi
fi
if [[ "$pin" == true ]]; then "$binary" --pin-to-taskbar; fi
echo "Installed Mail launcher, icons and mailto handler: $binary"
