#!/usr/bin/env bash
set -Eeuo pipefail

# DaisyOS deliberately keeps this policy in MiB. It mirrors the values users
# see in diagnostics and avoids rounding a 4 GB machine into an unusably small
# reserve because firmware-reported RAM is a little below the marketed amount.
total_mib="${DAISYOS_MEMORY_TOTAL_MIB:-}"

usage() {
  cat <<'EOF'
Usage: scripts/daisyos-memory-policy.sh <total-mib|reserve-mib|core-min-mib|apps-high-mib|status>

Reports the native-session memory policy. Set DAISYOS_MEMORY_TOTAL_MIB only
for deterministic tests; normal sessions read MemTotal from /proc/meminfo.
EOF
}

memory_total_mib() {
  if [[ -n "$total_mib" ]]; then
    [[ "$total_mib" =~ ^[0-9]+$ && "$total_mib" -gt 0 ]] || {
      echo "DAISYOS_MEMORY_TOTAL_MIB must be a positive whole number." >&2
      return 1
    }
    printf '%s\n' "$total_mib"
    return 0
  fi

  local kib
  kib="$(awk '/^MemTotal:/ { print $2; exit }' /proc/meminfo 2>/dev/null || true)"
  [[ "$kib" =~ ^[0-9]+$ && "$kib" -gt 0 ]] || {
    echo "Could not determine installed RAM from /proc/meminfo." >&2
    return 1
  }
  printf '%s\n' "$((kib / 1024))"
}

reserve_mib_for() {
  local installed_mib="$1"
  # Product policy: 4 GB → 1.25 GB, 8 GB → 1.5 GB, 16 GB → 2 GB,
  # 32 GB → 2.5 GB, and 64 GB+ → 3.5 GB. Intermediate hardware uses
  # the next safe tier so the system stays responsive instead of gambling on
  # a lower reserve.
  if (( installed_mib <= 4096 )); then
    printf '%s\n' 1280
  elif (( installed_mib <= 8192 )); then
    printf '%s\n' 1536
  elif (( installed_mib <= 16384 )); then
    printf '%s\n' 2048
  elif (( installed_mib <= 32768 )); then
    printf '%s\n' 2560
  else
    printf '%s\n' 3584
  fi
}

core_min_mib_for() {
  local reserve_mib="$1"
  local minimum=$((reserve_mib / 4))
  (( minimum < 256 )) && minimum=256
  (( minimum > 1024 )) && minimum=1024
  printf '%s\n' "$minimum"
}

apps_high_mib_for() {
  local installed_mib="$1"
  local reserve_mib="$2"
  local high=$((installed_mib - reserve_mib))
  # Do not create an impossible cgroup boundary on unusual, very small VMs.
  (( high < 256 )) && high=256
  printf '%s\n' "$high"
}

installed_mib="$(memory_total_mib)"
reserved_mib="$(reserve_mib_for "$installed_mib")"
core_min_mib="$(core_min_mib_for "$reserved_mib")"
apps_high_mib="$(apps_high_mib_for "$installed_mib" "$reserved_mib")"

case "${1:-status}" in
  total-mib) printf '%s\n' "$installed_mib" ;;
  reserve-mib) printf '%s\n' "$reserved_mib" ;;
  core-min-mib) printf '%s\n' "$core_min_mib" ;;
  apps-high-mib) printf '%s\n' "$apps_high_mib" ;;
  status)
    printf 'Installed RAM: %s MiB\n' "$installed_mib"
    printf 'Reserved headroom: %s MiB\n' "$reserved_mib"
    printf 'Critical shell hard protection: %s MiB\n' "$core_min_mib"
    printf 'Aggregate app memory high watermark: %s MiB\n' "$apps_high_mib"
    ;;
  --help|-h)
    usage
    ;;
  *)
    usage >&2
    exit 2
    ;;
esac
