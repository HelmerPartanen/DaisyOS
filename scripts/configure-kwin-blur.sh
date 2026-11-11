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

configured_strength=""
if command -v kreadconfig6 >/dev/null; then
  configured_strength="$(kreadconfig6 --file kwinrc --group Effect-blur --key BlurStrength --default '' 2>/dev/null || true)"
fi
if [[ ! "$configured_strength" =~ ^([1-9]|1[0-5])$ ]]; then
  configured_strength=1
fi

write_blur_config() {
  local config_file="$1"
  kwriteconfig6 --file "$config_file" --group Plugins --key blurEnabled --type bool true
  kwriteconfig6 --file "$config_file" --group Effect-blur --key BlurStrength --type int "$configured_strength"
  kwriteconfig6 --file "$config_file" --group Effect-blur --key NoiseStrength --type int 0
}

if [[ "$mode" == "--runtime" ]]; then
  # Respect the user's selected blur amount, while always removing KWin's
  # additive grain pass. A missing strength gets the DaisyOS default.
  write_blur_config kwinrc
else
  # System defaults cover the live ISO; skeleton defaults become the
  # persistent per-user config after installation and take precedence over
  # later distro defaults.
  install -d "$stage_root/etc/xdg" "$stage_root/etc/skel/.config"
  write_blur_config "$stage_root/etc/xdg/kwinrc"
  write_blur_config "$stage_root/etc/skel/.config/kwinrc"
fi
