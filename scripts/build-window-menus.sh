#!/usr/bin/env bash
set -Eeuo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
build_dir="$repo_root/src/native/daisy_window_menu/build"
cmake_args=(-DCMAKE_BUILD_TYPE=Release)
# Use the existing pinned native-effect dependencies where distributions omit
# development packages. Otherwise CMake uses the system development packages.
cache_root="${XDG_CACHE_HOME:-$HOME/.cache}/daisyos/kwin-frame-effect"
[[ ! -d "$cache_root/ecm-prefix" ]] || cmake_args+=(-DCMAKE_PREFIX_PATH="$cache_root/ecm-prefix")
[[ ! -d "$cache_root/vulkan-headers/include" ]] || cmake_args+=(-DVulkan_INCLUDE_DIR="$cache_root/vulkan-headers/include")
cmake_generator=()
if command -v ninja >/dev/null 2>&1; then
  cmake_generator=(-G Ninja)
fi
cmake -S "$repo_root/src/native/daisy_window_menu" -B "$build_dir" "${cmake_generator[@]}" "${cmake_args[@]}"
cmake --build "$build_dir"
