#!/usr/bin/env bash
set -Eeuo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
temp_dir="$(mktemp -d)"
trap 'rm -rf "$temp_dir"' EXIT

"$repo_root/scripts/install-memory-resilience.sh" --root "$temp_dir" --no-disk-swap --no-package --no-activate

cmp "$repo_root/scripts/memory/zram-generator.conf" "$temp_dir/etc/systemd/zram-generator.conf"
cmp "$repo_root/scripts/memory/10-daisyos-oomd.conf" "$temp_dir/etc/systemd/oomd.conf.d/10-daisyos-oomd.conf"
cmp "$repo_root/scripts/memory/90-daisyos-memory.conf" "$temp_dir/etc/sysctl.d/90-daisyos-memory.conf"
cmp "$repo_root/scripts/memory/daisyos-memory-health" "$temp_dir/usr/local/lib/daisyos/daisyos-memory-health"
cmp "$repo_root/scripts/memory/daisyos-memory-health.service" "$temp_dir/etc/systemd/system/daisyos-memory-health.service"
[[ "$(stat -c %a "$temp_dir/usr/local/lib/daisyos/daisyos-memory-health")" == 755 ]]

iso="$repo_root/os/archiso/DaisyOS/airootfs"
cmp "$repo_root/scripts/memory/zram-generator.conf" "$iso/etc/systemd/zram-generator.conf"
cmp "$repo_root/scripts/memory/10-daisyos-oomd.conf" "$iso/etc/systemd/oomd.conf.d/10-daisyos-oomd.conf"
cmp "$repo_root/scripts/memory/90-daisyos-memory.conf" "$iso/etc/sysctl.d/90-daisyos-memory.conf"
cmp "$repo_root/scripts/memory/daisyos-memory-health" "$iso/usr/local/lib/daisyos/daisyos-memory-health"
cmp "$repo_root/scripts/memory/daisyos-memory-health.service" "$iso/etc/systemd/system/daisyos-memory-health.service"
grep -Fq 'Wants=daisyos-memory-health.service' "$iso/etc/systemd/system/multi-user.target.d/20-daisyos-memory-health.conf"

if "$repo_root/scripts/install-memory-resilience.sh" --root relative >/dev/null 2>&1; then
  echo "Memory installer accepted a relative staging root." >&2
  exit 1
fi
if "$repo_root/scripts/install-memory-resilience.sh" --root "$temp_dir" --disk-swap-gib 33 >/dev/null 2>&1; then
  echo "Memory installer accepted an oversized fallback." >&2
  exit 1
fi

echo "DaisyOS memory-resilience tests passed."
