#!/usr/bin/env bash
set -euo pipefail

usage() {
  echo "Usage: $0 --runtime | --stage-root DIRECTORY" >&2
}

[[ $# -eq 1 || $# -eq 2 ]] || { usage; exit 2; }
mode="$1"
if [[ "$mode" == "--stage-root" ]]; then
  [[ $# -eq 2 ]] || { usage; exit 2; }
  stage_root="$2"
elif [[ "$mode" != "--runtime" || $# -ne 1 ]]; then
  usage
  exit 2
fi

command -v kwriteconfig6 >/dev/null || { echo 'kwriteconfig6 is required.' >&2; exit 1; }

readonly configured_strength=4

write_blur_config() {
  local config_file="$1"
  kwriteconfig6 --file "$config_file" --group Plugins --key blurEnabled --type bool true
  kwriteconfig6 --file "$config_file" --group Effect-blur --key BlurStrength --type int "$configured_strength"
  kwriteconfig6 --file "$config_file" --group Effect-blur --key NoiseStrength --type int 0
}

if [[ "$mode" == "--runtime" ]]; then
  # DaisyOS always uses the same blur strength and disables additive grain.
  write_blur_config kwinrc
else
  # Set both system and new-user configs so these values persist across live
  # sessions and installation.
  install -d "$stage_root/etc/xdg" "$stage_root/etc/skel/.config"
  write_blur_config "$stage_root/etc/xdg/kwinrc"
  write_blur_config "$stage_root/etc/skel/.config/kwinrc"
fi
