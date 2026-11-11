#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
destination="${repo_root}/publish/plasma-wallpaper-root"

if [[ "${1:-}" == "--root" ]]; then
  [[ $# -eq 2 ]] || { echo "Usage: $0 [--root DESTINATION]" >&2; exit 2; }
  destination="$2"
elif [[ $# -gt 0 ]]; then
  echo "Usage: $0 [--root DESTINATION]" >&2
  exit 2
fi

plugin_source="$repo_root/packaging/plasma/wallpapers/org.daisyos.wallpaper"
plugin_destination="$destination/usr/share/plasma/wallpapers/org.daisyos.wallpaper"

[[ -f "$plugin_source/metadata.json" && -f "$plugin_source/contents/ui/main.qml" ]] || {
  echo "DaisyOS Plasma wallpaper package is incomplete." >&2
  exit 1
}

install -d "$plugin_destination/contents/config" "$plugin_destination/contents/ui"
install -m644 "$plugin_source/metadata.json" "$plugin_destination/metadata.json"
install -m644 "$plugin_source/contents/config/main.xml" "$plugin_destination/contents/config/main.xml"
install -m644 "$plugin_source/contents/ui/main.qml" "$plugin_destination/contents/ui/main.qml"
install -m644 "$plugin_source/contents/ui/config.qml" "$plugin_destination/contents/ui/config.qml"
