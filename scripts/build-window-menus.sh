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
cmake_fresh=()
# A build tree may have been configured with Ninja on another image. CMake
# otherwise keeps that unavailable generator forever and blocks menu updates.
if [[ -f "$build_dir/CMakeCache.txt" ]] &&
   { grep -Eq '^CMAKE_MAKE_PROGRAM:FILEPATH=.*-NOTFOUND$' "$build_dir/CMakeCache.txt" ||
     { ! command -v ninja >/dev/null 2>&1 && grep -Eq '^CMAKE_GENERATOR:INTERNAL=Ninja$' "$build_dir/CMakeCache.txt"; }; }; then
  cmake_fresh=(--fresh)
fi
kwin_plugin_version="$(sed -n 's/^#define KWIN_PLUGIN_VERSION_STRING "\(.*\)"$/\1/p' /usr/include/kwin/config-kwin.h 2>/dev/null | head -n 1)"
if [[ -n "$kwin_plugin_version" && -f "$build_dir/daisyos-window-menus-v4.so" ]] &&
   ! grep -a -Fq "org.kde.kwin.EffectPluginFactory${kwin_plugin_version}" "$build_dir/daisyos-window-menus-v4.so"; then
  # Qt AUTOMOC does not re-run moc when external KWin headers upgrade.
  rm -rf -- "$build_dir"
  cmake_fresh=()
fi
cmake "${cmake_fresh[@]}" -S "$repo_root/src/native/daisy_window_menu" -B "$build_dir" "${cmake_generator[@]}" "${cmake_args[@]}"
cmake --build "$build_dir"
