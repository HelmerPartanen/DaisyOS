#!/usr/bin/env bash
set -euo pipefail

report="${1:?Usage: scripts/check-shell-performance-report.sh REPORT}"
[[ -s "$report" ]] || { echo "Missing performance report: $report" >&2; exit 2; }

bytes_to_mib() { awk -v value="$1" -v unit="$2" 'BEGIN { if (unit == "GiB") print value * 1024; else if (unit == "MiB") print value; else if (unit == "KiB") print value / 1024; else print value / 1048576 }'; }
metric_mib() {
  local label="$1" value unit
  read -r value unit < <(awk -v label="$label" '$0 ~ label { value=$(NF-1); unit=$NF } END { print value, unit }' "$report") || true
  [[ -n "${value:-}" ]] || { echo "Report does not contain $label" >&2; exit 2; }
  bytes_to_mib "$value" "$unit"
}

private_mib="$(metric_mib '^Private memory')"
rss_mib="$(metric_mib '^Working set')"
interactive_ms="$(awk '/^Interactive in/ { value=$(NF-1) } END { print value }' "$report")"
idle_cpu="$(awk '/^Idle CPU average/ { value=$(NF-1) } END { print value }' "$report")"
[[ "$interactive_ms" =~ ^-?[0-9]+([.][0-9]+)?$ ]] || { echo "Report has no interactive timing." >&2; exit 2; }
[[ "$idle_cpu" =~ ^[0-9]+([.][0-9]+)?$ ]] || { echo "Report has no idle CPU average." >&2; exit 2; }

awk -v value="$private_mib" 'BEGIN { exit value <= 500 ? 0 : 1 }' || { echo "Private memory budget exceeded: ${private_mib} MiB > 500 MiB" >&2; exit 1; }
awk -v value="$rss_mib" 'BEGIN { exit value <= 650 ? 0 : 1 }' || { echo "RSS budget exceeded: ${rss_mib} MiB > 650 MiB" >&2; exit 1; }
awk -v value="$interactive_ms" 'BEGIN { exit value >= 0 && value <= 3000 ? 0 : 1 }' || { echo "Startup budget exceeded: ${interactive_ms} ms > 3000 ms" >&2; exit 1; }
awk -v value="$idle_cpu" 'BEGIN { exit value <= 2 ? 0 : 1 }' || { echo "Idle CPU budget exceeded: ${idle_cpu}% > 2%" >&2; exit 1; }
echo "Shell performance budgets passed: ${rss_mib} MiB RSS, ${private_mib} MiB private, ${interactive_ms} ms interactive, ${idle_cpu}% idle CPU."
