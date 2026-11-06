#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"
python3 scripts/generate-slint-tokens.py --check
python3 scripts/generate-slint-locales.py --check
build_mode=()
case "${1:-}" in
  '') ;;
  --release) build_mode=(--release) ;;
  *) echo 'Usage: scripts/build-rust-ui.sh [--release]' >&2; exit 2 ;;
esac
CARGO_BUILD_JOBS="${CARGO_BUILD_JOBS:-2}" cargo build --locked "${build_mode[@]}" -p daisy-calculator -p daisy-ui-gallery
