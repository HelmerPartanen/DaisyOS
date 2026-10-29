#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
output="${1:-$repo_root/artifacts/continuity/native}"
mkdir -p "$output"
read -r -a flags <<< "$(pkg-config --cflags --libs gio-unix-2.0 gstreamer-app-1.0 libei-1.0 xkbcommon)"
cc -std=c11 -Wall -Wextra -Werror -Wno-misleading-indentation -O2 \
  "$repo_root/src/native/daisy_continuity_media/main.c" "${flags[@]}" -lm -o "$output/daisyos-continuity-media"
