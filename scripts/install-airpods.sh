#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
if [[ "${1:-}" != --install ]]; then
  "$repo_root/scripts/build-librepods.sh"
  echo "Built AirPods support. Install with: sudo scripts/install-airpods.sh --install"
  exit 0
fi
[[ "${EUID:-$(id -u)}" == 0 ]] || { echo "Installation requires root." >&2; exit 1; }
for package in bluez bluez-utils qt6-connectivity qt6-declarative libpulse openssl wireplumber; do
  pacman -Q "$package" >/dev/null || { echo "Missing dependency: $package" >&2; exit 1; }
done
"$repo_root/scripts/stage-airpods.sh" --root /
systemctl daemon-reload
systemctl enable --now bluetooth.service
echo "Installed. Log out and back into DaisyOS to start the AirPods companion."
