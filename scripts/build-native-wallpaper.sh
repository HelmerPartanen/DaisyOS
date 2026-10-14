#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
native_dir="$repo_root/src/native/daisy_live_wallpaper"
build_dir="$native_dir/build"

echo "Building DaisyOS Native Live Wallpaper Library..."
mkdir -p "$build_dir"
cd "$build_dir"

cmake -DCMAKE_BUILD_TYPE=Release ..
make -j$(nproc)

echo "Native build complete:"
ls -l "$build_dir/libdaisy_live_wallpaper.so" "$build_dir/daisy_wallpaper_test"
