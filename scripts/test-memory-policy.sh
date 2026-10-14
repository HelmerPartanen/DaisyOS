#!/usr/bin/env bash
set -Eeuo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
policy="$repo_root/scripts/daisyos-memory-policy.sh"

assert_policy() {
  local installed="$1"
  local reserve="$2"
  local core_min="$3"
  local apps_high="$4"

  [[ "$(DAISYOS_MEMORY_TOTAL_MIB="$installed" "$policy" reserve-mib)" == "$reserve" ]]
  [[ "$(DAISYOS_MEMORY_TOTAL_MIB="$installed" "$policy" core-min-mib)" == "$core_min" ]]
  [[ "$(DAISYOS_MEMORY_TOTAL_MIB="$installed" "$policy" apps-high-mib)" == "$apps_high" ]]
}

assert_policy 4096 1280 320 2816
assert_policy 8192 1536 384 6656
assert_policy 16384 2048 512 14336
assert_policy 32768 2560 640 30208
assert_policy 65536 3584 896 61952

echo "DaisyOS memory-policy tests passed."
