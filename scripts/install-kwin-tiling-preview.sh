#!/usr/bin/env bash
set -Eeuo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
command -v kwriteconfig6 >/dev/null || { echo 'kwriteconfig6 is required.' >&2; exit 1; }
data_root="${XDG_DATA_HOME:-$HOME/.local/share}"
relative_path=kwin-wayland/outline/daisyos
install -d "$data_root/$relative_path"
install -m 644 "$repo_root/packaging/kwin/daisyos-tiling-preview/outline.qml" "$repo_root/packaging/kwin/daisyos-tiling-preview/PreviewFrame.qml" "$data_root/$relative_path/"
kwriteconfig6 --file kwinrc --group Outline --key QmlPath "$relative_path/outline.qml"
if command -v qdbus6 >/dev/null; then
    timeout 3 qdbus6 org.kde.KWin /KWin org.kde.KWin.reconfigure >/dev/null 2>&1 || true
fi
# KWin caches OutlineVisual for the lifetime of the compositor. Do not restart
# a user's compositor (or pretend that reconfigure replaces an existing helper).
echo 'Installed DaisyOS tiling preview. If a preview was already shown in this KWin session, it takes effect at the next login.'
