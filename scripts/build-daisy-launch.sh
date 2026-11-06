#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd -- "$(dirname -- "$0")/.." && pwd)"
cmake_generator=()
if command -v ninja >/dev/null 2>&1; then
  cmake_generator=(-G Ninja)
fi
cmake -S "$repo_root/services/DaisyLaunch" -B "$repo_root/services/DaisyLaunch/build" "${cmake_generator[@]}" -DCMAKE_BUILD_TYPE=Release -DDAISY_LAUNCH_LEGACY=OFF
cmake --build "$repo_root/services/DaisyLaunch/build"
