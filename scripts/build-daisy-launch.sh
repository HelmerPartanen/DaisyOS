#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd -- "$(dirname -- "$0")/.." && pwd)"
cmake_generator=()
# Retain a working cached generator when build tools change between logins.
# Passing -G Ninja to an existing Makefiles tree aborts session startup.
if [[ -f "$repo_root/services/DaisyLaunch/build/CMakeCache.txt" ]] &&
   grep -Fqx 'CMAKE_GENERATOR:INTERNAL=Unix Makefiles' "$repo_root/services/DaisyLaunch/build/CMakeCache.txt" &&
   command -v make >/dev/null 2>&1; then
  cmake_generator=(-G 'Unix Makefiles')
elif command -v ninja >/dev/null 2>&1; then
  cmake_generator=(-G Ninja)
fi
cmake -S "$repo_root/services/DaisyLaunch" -B "$repo_root/services/DaisyLaunch/build" "${cmake_generator[@]}" -DCMAKE_BUILD_TYPE=Release -DDAISY_LAUNCH_LEGACY=OFF
cmake --build "$repo_root/services/DaisyLaunch/build"
