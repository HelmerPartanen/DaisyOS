#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
native_dir="$repo_root/src/native/daisy_idle_monitor"
build_dir="$native_dir/build"

echo "Building DaisyOS Native Idle Monitor..."
mkdir -p "$build_dir"
cd "$build_dir"

cmake -DCMAKE_BUILD_TYPE=Release ..
make -j$(nproc)

echo "Native idle monitor build complete:"
ls -l "$build_dir/libdaisy_idle.so" "$build_dir/daisyos-idle-monitor"
