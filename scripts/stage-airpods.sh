#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
[[ "${1:-}" == --root && -n "${2:-}" ]] || { echo "Usage: $0 --root STAGING_ROOT [BUILD_OUTPUT]" >&2; exit 2; }
stage_root="$2"
payload="${3:-$repo_root/artifacts/librepods}"
[[ -x "$payload/librepods" && -f "$payload/source.tar.gz" ]] || { echo "Build the AirPods companion first: scripts/build-librepods.sh" >&2; exit 1; }
install -Dm755 "$payload/librepods" "$stage_root/usr/lib/daisyos/librepods"
for file in LICENSE revision.txt source.tar.gz; do
  install -Dm644 "$payload/$file" "$stage_root/usr/share/daisyos/librepods/$file"
done
install -Dm644 "$repo_root/packaging/librepods/daisyos-librepods.service" "$stage_root/usr/lib/systemd/user/daisyos-librepods.service"
mkdir -p "$stage_root/usr/lib/systemd/user/graphical-session.target.wants" "$stage_root/etc/systemd/system/bluetooth.target.wants" "$stage_root/etc/systemd/system/multi-user.target.wants"
ln -sfn ../daisyos-librepods.service "$stage_root/usr/lib/systemd/user/graphical-session.target.wants/daisyos-librepods.service"
ln -sfn /usr/lib/systemd/system/bluetooth.service "$stage_root/etc/systemd/system/bluetooth.target.wants/bluetooth.service"
ln -sfn /usr/lib/systemd/system/bluetooth.service "$stage_root/etc/systemd/system/multi-user.target.wants/bluetooth.service"
ln -sfn /usr/lib/systemd/system/bluetooth.service "$stage_root/etc/systemd/system/dbus-org.bluez.service"
# BlueZ's default reconnect policy already covers A2DP and respects an explicit disconnect.
# Enable AVRCP via WirePlumber rather than running the conflicting mpris-proxy.
install -Dm644 "$repo_root/packaging/librepods/51-airpods-avrcp.conf" "$stage_root/usr/share/wireplumber/wireplumber.conf.d/51-airpods-avrcp.conf"
