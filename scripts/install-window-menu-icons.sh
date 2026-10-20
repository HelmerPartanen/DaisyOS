#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
case "${1:-}" in
  --user) destination="${XDG_DATA_HOME:-$HOME/.local/share}/daisyos/window-menu-icons" ;;
  --root) [[ $# == 2 && -n "$2" ]] || exit 2; destination="$2/usr/share/daisyos/window-menu-icons" ;;
  *) echo 'Usage: install-window-menu-icons.sh --user | --root PATH' >&2; exit 2 ;;
esac
install -d "$destination"
for name in Maximize arrow-shrink Minimize cancel-01 arrow-up-02 arrow-down-02 drag-drop arrow-expand full-screen square-dashed view-off keyboard settings-01 more-vertical computer square-stack; do
  install -m644 "$repo_root/src/DaisyOS.Shell/Assets/Icons/HugeIcons/$name.svg" "$destination/$name.svg"
done
