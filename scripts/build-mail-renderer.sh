#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
build="$repo_root/src/native/daisy_mail_renderer/build"
mkdir -p "$build"
source_file="$repo_root/src/native/daisy_mail_renderer/main.c"
output="$build/daisy-mail-renderer"
if [[ -x "$output" && "$output" -nt "$source_file" && "$output" -nt "${BASH_SOURCE[0]}" ]]; then exit 0; fi
read -ra renderer_flags <<< "$(pkg-config --cflags --libs webkit2gtk-4.1)"
cc -std=c11 -Wall -Wextra -Werror -Wno-deprecated-declarations -O2 "$source_file" "${renderer_flags[@]}" -o "$output.tmp"
mv "$output.tmp" "$output"
