#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd -- "$(dirname -- "$0")/.." && pwd)"
"$repo_root/scripts/build-daisy-launch.sh"
data_home="${XDG_DATA_HOME:-$HOME/.local/share}"
config_home="${XDG_CONFIG_HOME:-$HOME/.config}"
install -Dm755 "$repo_root/services/DaisyLaunch/build/daisy-launch" "$HOME/.local/bin/daisy-launch"
install -Dm644 "$repo_root/packaging/systemd/daisy-launch.service" "$config_home/systemd/user/daisy-launch.service"
mkdir -p "$data_home/dbus-1/services"
cat > "$data_home/dbus-1/services/org.daisyos.Launcher1.service" <<SERVICE
[D-BUS Service]
Name=org.daisyos.Launcher1
Exec=$HOME/.local/bin/daisy-launch
SystemdService=daisy-launch.service
SERVICE
systemctl --user daemon-reload
systemctl --user enable daisy-launch.service
systemctl --user restart daisy-launch.service
