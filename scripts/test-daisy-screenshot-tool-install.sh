#!/usr/bin/env bash
set -Eeuo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
installer="$repo_root/scripts/install-daisy-screenshot-tool.sh"
temporary_root="$(mktemp -d)"
trap 'rm -rf "$temporary_root"' EXIT

"$installer" --prefix "$temporary_root/stage" --debug

helper="$temporary_root/stage/libexec/daisyos/daisy-screenshot-helper"
desktop_file="$temporary_root/stage/share/applications/org.daisyos.screenshot-helper.desktop"
[[ -x "$helper" ]]
[[ -f "$desktop_file" ]]
[[ -f "$helper.api-v2" ]]
grep -Fxq "Exec=$helper" "$desktop_file"
grep -Fxq 'X-KDE-DBUS-Restricted-Interfaces=org.kde.KWin.ScreenShot2' "$desktop_file"
"$helper" --help >/dev/null

if command -v desktop-file-validate >/dev/null 2>&1; then
  desktop-file-validate "$desktop_file"
fi

echo "DaisyOS screenshot helper installer tests passed."
