#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
test_root="$(mktemp -d)"
trap 'rm -rf "$test_root"' EXIT
DBUS_SESSION_BUS_ADDRESS= HOME="$test_root/home" XDG_DATA_HOME="$test_root/data" "$repo_root/scripts/install-system-apps.sh"
for application in Files Notes Settings DiskManager; do
  lower="${application,,}"
  [[ -x "$test_root/home/.local/bin/daisyos-$lower" ]]
  rg -q "^Exec=\".*DaisyOS.$application\"" "$test_root/data/applications/org.daisyos.$lower.desktop"
  if command -v desktop-file-validate >/dev/null 2>&1; then
    desktop-file-validate "$test_root/data/applications/org.daisyos.$lower.desktop"
  fi
done
"$repo_root/scripts/test-files-app-package.sh"
echo 'Standalone application installation checks passed.'
