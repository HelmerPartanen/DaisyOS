#!/usr/bin/env bash
set -Eeuo pipefail
# Let KWin own work areas, output coordinates, client size constraints and
# quick tiling. Never reposition a moving window from a competing script.
reconfigure=true
case "${1:-}" in
  '') [[ $# == 0 ]] || exit 2 ;;
  --no-reconfigure) [[ $# == 1 ]] || exit 2; reconfigure=false ;;
  *) echo 'Usage: configure-kwin-snapping.sh [--no-reconfigure]' >&2; exit 2 ;;
esac
command -v kwriteconfig6 >/dev/null || { echo 'kwriteconfig6 is required.' >&2; exit 1; }
# No attraction to arbitrary app edges or the center of the display. A small
# edge zone only catches a window after it crosses the usable screen boundary.
kwriteconfig6 --file kwinrc --group Windows --key BorderSnapZone --type int 8
kwriteconfig6 --file kwinrc --group Windows --key WindowSnapZone --type int 0
kwriteconfig6 --file kwinrc --group Windows --key CenterSnapZone --type int 0
kwriteconfig6 --file kwinrc --group Windows --key SnapOnlyWhenOverlapping --type bool true
# Pointer at a side = half; at the top = maximize. Quarter targets occupy only
# the top/bottom 10% of the side edge, rather than the default 25%.
kwriteconfig6 --file kwinrc --group Windows --key ElectricBorderTiling --type bool true
kwriteconfig6 --file kwinrc --group Windows --key ElectricBorderMaximize --type bool true
kwriteconfig6 --file kwinrc --group Windows --key ElectricBorderCornerRatio --type double 0.10
if "$reconfigure" && command -v qdbus6 >/dev/null; then
  timeout 3 qdbus6 org.kde.KWin /KWin org.kde.KWin.reconfigure >/dev/null 2>&1 || true
fi
