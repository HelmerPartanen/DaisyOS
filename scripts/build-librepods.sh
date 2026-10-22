#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
output="${1:-$repo_root/artifacts/librepods}"
revision="$(cat "$repo_root/packaging/librepods/revision.txt")"
fingerprint="$(cat "$repo_root/packaging/librepods/revision.txt" "$repo_root/packaging/librepods/patch-source.py" "$repo_root/packaging/librepods/shell-bridge.hpp" "$repo_root/scripts/build-librepods.sh" | sha256sum | cut -d' ' -f1)"
if [[ -x "$output/librepods" && -s "$output/source.tar.gz" && -f "$output/build-fingerprint.txt" && "$(cat "$output/build-fingerprint.txt")" == "$fingerprint" ]]; then
  echo "LibrePods companion is up to date: $output"
  exit 0
fi
build_root="$(mktemp -d)"
trap 'rm -rf "$build_root"' EXIT
# No moving branch or downloaded executable: build the reviewed revision.
git init -q "$build_root/source"
git -C "$build_root/source" fetch -q --depth 1 https://github.com/librepods-org/librepods.git "$revision"
git -C "$build_root/source" checkout -q --detach FETCH_HEAD
[[ "$(git -C "$build_root/source" rev-parse HEAD)" == "$revision" ]]
cp "$repo_root/packaging/librepods/shell-bridge.hpp" "$build_root/source/linux/"
python3 "$repo_root/packaging/librepods/patch-source.py" "$build_root/source"
cmake -S "$build_root/source/linux" -B "$build_root/build" -DCMAKE_BUILD_TYPE=Release
cmake --build "$build_root/build" --target librepods --parallel "${DAISYOS_BUILD_JOBS:-2}"
mkdir -p "$output"
install -m755 "$build_root/build/librepods" "$output/librepods"
install -m644 "$build_root/source/LICENSE" "$output/LICENSE"
cp "$repo_root/packaging/librepods/revision.txt" "$output/"
# Supply corresponding modified source alongside distributed GPL binaries.
tar -C "$build_root/source" --exclude=.git -czf "$output/source.tar.gz" linux LICENSE
printf '%s\n' "$fingerprint" > "$output/build-fingerprint.txt"
