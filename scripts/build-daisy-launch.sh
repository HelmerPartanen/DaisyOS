#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd -- "$(dirname -- "$0")/.." && pwd)"
cmake -S "$repo_root/services/DaisyLaunch" -B "$repo_root/services/DaisyLaunch/build" -G Ninja -DCMAKE_BUILD_TYPE=Release
cmake --build "$repo_root/services/DaisyLaunch/build"
