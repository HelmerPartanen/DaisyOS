#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
manifest="$repo_root/src/native/daisy_background/Cargo.toml"
build_dir="$repo_root/src/native/daisy_idle_monitor/build"
# Fix the output directory even if the caller sets CARGO_TARGET_DIR.
target_dir="$repo_root/src/native/daisy_background/target"
cargo build --manifest-path "$manifest" --target-dir "$target_dir" --release --locked
mkdir -p "$build_dir"
install -m755 "$target_dir/release/libdaisy_idle.so" "$build_dir/libdaisy_idle.so"
install -m755 "$target_dir/release/daisyos-idle-monitor" "$build_dir/daisyos-idle-monitor"
echo "Built Rust idle monitor and C-compatible library in $build_dir"
