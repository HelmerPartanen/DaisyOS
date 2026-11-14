#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
app_configuration="${DAISYOS_APP_CONFIGURATION:-Release}"
[[ "$app_configuration" == Release || "$app_configuration" == Debug ]] || { echo "DAISYOS_APP_CONFIGURATION must be Release or Debug." >&2; exit 1; }
files_binary="${1:-$repo_root/src/DaisyOS.Files/bin/$app_configuration/net10.0/DaisyOS.Files}"
if [[ $# -eq 0 && ! -x "$files_binary" ]] && command -v dotnet >/dev/null 2>&1; then
  dotnet build "$repo_root/src/DaisyOS.Files/DaisyOS.Files.csproj" -c "$app_configuration" --disable-build-servers -m:1 -v:minimal
fi
data_home="${XDG_DATA_HOME:-$HOME/.local/share}"
removed_apps="$data_home/daisyos/applications/removed-optional-applications"
mkdir -p "$HOME/.local/bin" "$data_home/applications"
if [[ ! -f "$removed_apps" ]] || ! grep -q -i -x Files "$removed_apps"; then
  "$repo_root/scripts/install-files-app.sh" "$files_binary"
fi
python3 "$repo_root/scripts/install-user-gaming-integration.py"
data_home="${XDG_DATA_HOME:-$HOME/.local/share}"
removed_apps="$data_home/daisyos/applications/removed-optional-applications"
for application in Mail Notes Settings DiskManager AppInstaller Calculator AppStore UIKit; do
  if [[ -f "$removed_apps" ]] && grep -q -i -x "$application" "$removed_apps"; then
    echo "Keeping intentionally removed optional application absent: $application"
    continue
  fi
  binary="$repo_root/src/DaisyOS.$application/bin/$app_configuration/net10.0/DaisyOS.$application"
  if [[ $# -gt 0 ]]; then binary="$(dirname "$1")/../$application/DaisyOS.$application"; fi
  if [[ $# -eq 0 && ! -x "$binary" ]] && command -v dotnet >/dev/null 2>&1; then
    dotnet build "$repo_root/src/DaisyOS.$application/DaisyOS.$application.csproj" -c "$app_configuration" --disable-build-servers -m:1 -v:minimal
  fi
  [[ -x "$binary" ]] || { echo "Build DaisyOS.$application before installing it: $binary" >&2; exit 1; }
  binary="$(realpath "$binary")"
  if [[ "$application" == Mail ]]; then
    "$repo_root/scripts/install-mail-app.sh" "$binary"
    continue
  fi
  lower="${application,,}"
  icon_name="$application"
  [[ "$application" != DiskManager ]] || icon_name=Disk_Manager
  [[ "$application" != AppStore ]] || icon_name=Store
  [[ "$application" != UIKit ]] || icon_name=UiKit
  icon_suffix=""
  [[ "$application" != DiskManager ]] || icon_suffix=_Dark
  install -Dm0644 "$repo_root/src/DaisyOS.Shell/Assets/AppIcons/${icon_name}${icon_suffix}.png" "$data_home/icons/hicolor/1024x1024/apps/org.daisyos.$lower.png"
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
python3 "$repo_root/scripts/register-development-applications.py" "$repo_root" "$app_configuration"
python3 "$repo_root/scripts/update-system-app-shortcuts.py" "$repo_root" "$app_configuration"

# Register AppImage MIME definition and associations
mkdir -p "$data_home/mime/packages"
cat > "$data_home/mime/packages/org.daisyos.appimage.xml" <<'XML'
<?xml version="1.0" encoding="utf-8"?>
<mime-info xmlns="http://www.freedesktop.org/standards/shared-mime-info">
  <mime-type type="application/vnd.appimage">
    <comment>AppImage application</comment>
    <glob pattern="*.AppImage"/>
    <glob pattern="*.appimage"/>
  </mime-type>
  <mime-type type="application/x-iso9660-appimage">
    <comment>ISO 9660 AppImage application</comment>
    <glob pattern="*.AppImage"/>
    <glob pattern="*.appimage"/>
  </mime-type>
  <mime-type type="application/x-daisyos-arch-package"><comment>Arch Linux application package</comment><glob pattern="*.pkg.tar"/><glob pattern="*.pkg.tar.zst"/><glob pattern="*.pkg.tar.xz"/><glob pattern="*.pkg.tar.gz"/></mime-type>
</mime-info>
XML

command -v update-mime-database >/dev/null 2>&1 && update-mime-database "$data_home/mime" || true
command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "$data_home/applications" || true

if command -v xdg-mime >/dev/null 2>&1; then
  xdg-mime default org.daisyos.appinstaller.desktop application/vnd.appimage 2>/dev/null || true
  xdg-mime default org.daisyos.appinstaller.desktop application/x-iso9660-appimage 2>/dev/null || true
fi

python3 - <<'PY'
import os
from pathlib import Path

config_home = os.environ.get("XDG_CONFIG_HOME") or os.path.expanduser("~/.config")
data_home = os.environ.get("XDG_DATA_HOME") or os.path.expanduser("~/.local/share")

for target_dir in [config_home, os.path.join(data_home, "applications")]:
    os.makedirs(target_dir, exist_ok=True)
    mimeapps = Path(target_dir) / "mimeapps.list"
    lines = mimeapps.read_text().splitlines() if mimeapps.exists() else []
    
    sections = {}
    current = None
    for line in lines:
        stripped = line.strip()
        if stripped.startswith("[") and stripped.endswith("]"):
            current = stripped
            sections.setdefault(current, [])
        elif current:
            sections[current].append(line)
            
    defaults = sections.setdefault("[Default Applications]", [])
    added = sections.setdefault("[Added Associations]", [])
    
    for m in ["application/vnd.appimage", "application/x-iso9660-appimage", "application/vnd.flatpak.ref", "application/vnd.flatpak", "application/vnd.debian.binary-package", "application/x-rpm", "application/x-daisyos-arch-package"]:
        defaults = [l for l in defaults if not l.startswith(f"{m}=")]
        defaults.append(f"{m}=org.daisyos.appinstaller.desktop")
        added = [l for l in added if not l.startswith(f"{m}=")]
        added.append(f"{m}=org.daisyos.appinstaller.desktop;")
        
    sections["[Default Applications]"] = defaults
    sections["[Added Associations]"] = added
    
    out = []
    for s, content in sections.items():
        out.append(s)
        out.extend(content)
        out.append("")
    mimeapps.write_text("\n".join(out).strip() + "\n")
PY
