#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
session_runner="$repo_root/scripts/run-shell-in-kwin.sh"

if ! command -v kwin_wayland >/dev/null 2>&1; then
  echo "KWin Wayland is missing. Install the packages listed in os/packages/kwin-wayland-prototype.txt."
  exit 1
fi

if [[ -z "${WAYLAND_DISPLAY:-}" && -z "${DISPLAY:-}" ]]; then
  echo "No graphical development session was found. Run this nested prototype from an existing desktop."
  exit 1
fi

parent_options=()
if [[ -n "${WAYLAND_DISPLAY:-}" ]]; then
  parent_options+=(--wayland-display "$WAYLAND_DISPLAY")
elif [[ -n "${DISPLAY:-}" ]]; then
  parent_options+=(--x11-display "$DISPLAY")
fi

socket_name="daisyos-kwin-$$"

if command -v kwriteconfig6 >/dev/null 2>&1; then
  kwriteconfig6 --file kwinrc --group Plugins --key blurEnabled true
  kwriteconfig6 --file kwinrc --group Effect-blur --key BlurStrength 4
fi

echo "Starting a 1440x900 windowed KWin prototype. Closing DaisyOS will close only the nested session."
exec kwin_wayland \
  "${parent_options[@]}" \
  --socket "$socket_name" \
  --xwayland \
  --locale1 \
  --width 1440 \
  --height 900 \
  --output-count 1 \
  --no-lockscreen \
  --exit-with-session "$session_runner"
