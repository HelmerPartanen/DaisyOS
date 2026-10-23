#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
"$repo_root/scripts/build-portal-capture.sh"
build="$repo_root/src/native/daisy_polkit_agent/build"
mkdir -p "$build"
read -r -a agent_flags <<< "$(pkg-config --cflags --libs polkit-agent-1 gio-unix-2.0)"
cc -std=c11 -Wall -Wextra -Werror -O2 "$repo_root/src/native/daisy_polkit_agent/main.c" "${agent_flags[@]}" -o "$build/daisyos-polkit-agent"

cmake_generator=()
if command -v ninja >/dev/null 2>&1; then
  cmake_generator=(-G Ninja)
fi
cmake -S "$repo_root/src/native/daisy_screenshot" -B "$repo_root/src/native/daisy_screenshot/build" "${cmake_generator[@]}" -DCMAKE_BUILD_TYPE=Release
cmake --build "$repo_root/src/native/daisy_screenshot/build"
cmake -S "$repo_root/src/native/daisy_portal_shortcuts" -B "$repo_root/src/native/daisy_portal_shortcuts/build" "${cmake_generator[@]}" -DCMAKE_BUILD_TYPE=Release
cmake --build "$repo_root/src/native/daisy_portal_shortcuts/build"
