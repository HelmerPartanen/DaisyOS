#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd -- "$(dirname -- "$0")/.." && pwd)"
crate="$repo_root/services/DaisyGaming/rust"
cargo build --manifest-path "$crate/Cargo.toml" --release --locked
