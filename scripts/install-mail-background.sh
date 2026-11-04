#!/usr/bin/env bash
# Installs (or removes with --remove) the per-user timer that checks DaisyOS Mail for new messages.
set -euo pipefail
dir="${XDG_CONFIG_HOME:-$HOME/.config}/systemd/user"
src="$(cd "$(dirname "$0")/../packaging/systemd" && pwd)"
if [[ "${1:-}" == "--remove" ]]; then
  systemctl --user disable --now daisyos-mail-check.timer || true
  rm -f "$dir"/daisyos-mail-check.{service,timer}
  systemctl --user daemon-reload; exit 0
fi
mkdir -p "$dir"
install -m 644 "$src"/daisyos-mail-check.{service,timer} "$dir"/
systemctl --user daemon-reload
systemctl --user enable --now daisyos-mail-check.timer
echo "Mail background check enabled (every 3 minutes)."
