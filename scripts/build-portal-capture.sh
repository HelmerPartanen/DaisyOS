#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
build="$repo_root/src/native/daisy_portal_capture/build"
mkdir -p "$build"
wayland-scanner client-header "$repo_root/src/native/daisy_portal_capture/screencast.xml" "$build/screencast-client.h"
wayland-scanner private-code "$repo_root/src/native/daisy_portal_capture/screencast.xml" "$build/screencast-protocol.c"
wayland-scanner client-header "$repo_root/src/native/daisy_portal_capture/xdg-output-unstable-v1.xml" "$build/xdg-output-client.h"
wayland-scanner private-code "$repo_root/src/native/daisy_portal_capture/xdg-output-unstable-v1.xml" "$build/xdg-output-protocol.c"
read -r -a capture_flags <<< "$(pkg-config --cflags --libs wayland-client)"
cc -std=c11 -Wall -Wextra -Werror -O2 -I"$build" "$repo_root/src/native/daisy_portal_capture/main.c" "$build/screencast-protocol.c" "$build/xdg-output-protocol.c" "${capture_flags[@]}" -o "$build/daisyos-portal-capture"
