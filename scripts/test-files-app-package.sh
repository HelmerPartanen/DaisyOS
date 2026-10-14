#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
test_root="$(mktemp -d)"
trap 'rm -rf "$test_root"' EXIT
DBUS_SESSION_BUS_ADDRESS= HOME="$test_root/home" XDG_DATA_HOME="$test_root/data" "$repo_root/scripts/install-files-app.sh"
[[ -x "$test_root/home/.local/bin/daisyos-files" ]]
rg -q '^Exec=".*DaisyOS.Files" %U$' "$test_root/data/applications/org.daisyos.files.desktop"
rg -q '^Name=org.freedesktop.FileManager1$' "$test_root/data/dbus-1/services/org.freedesktop.FileManager1.service"
if command -v desktop-file-validate >/dev/null 2>&1; then
  desktop-file-validate "$test_root/data/applications/org.daisyos.files.desktop"
fi
# Installing Files must preserve a pre-existing third-party activation service.
printf '[D-BUS Service]\nName=org.freedesktop.FileManager1\nExec=existing-file-manager\n' > "$test_root/data/dbus-1/services/org.freedesktop.FileManager1.service"
DBUS_SESSION_BUS_ADDRESS= HOME="$test_root/home" XDG_DATA_HOME="$test_root/data" "$repo_root/scripts/install-files-app.sh"
rg -q '^Exec=existing-file-manager$' "$test_root/data/dbus-1/services/org.freedesktop.FileManager1.service"
echo 'Files package installation checks passed.'
