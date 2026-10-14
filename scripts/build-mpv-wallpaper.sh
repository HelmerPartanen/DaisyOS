#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
local_prefix="${DAISYOS_MPV_PREFIX:-${XDG_DATA_HOME:-$HOME/.local/share}/DaisyOS/mpv-gpu}"
if ! pkg-config --exists mpv && [[ -d "$local_prefix/lib/pkgconfig" ]]; then
  export PKG_CONFIG_PATH="$local_prefix/lib/pkgconfig${PKG_CONFIG_PATH:+:$PKG_CONFIG_PATH}"
fi
if ! pkg-config --exists mpv; then
  echo "libmpv development files are required. Install mpv or run scripts/build-local-mpv.sh." >&2
  exit 1
fi
cmake -S "$repo_root/src/native/daisy_mpv_wallpaper" -B "$repo_root/src/native/daisy_mpv_wallpaper/build" -DCMAKE_BUILD_TYPE=Release -U '*MPV*'
cmake --build "$repo_root/src/native/daisy_mpv_wallpaper/build" -j 4
