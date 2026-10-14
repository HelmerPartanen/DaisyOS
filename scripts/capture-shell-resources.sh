#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
output="${1:-$repo_root/artifacts/diagnostics/shell-resources.txt}"
delay="${DAISYOS_RESOURCE_REPORT_DELAY_SECONDS:-30}"

if [[ -z "${DISPLAY:-}" && -z "${WAYLAND_DISPLAY:-}" ]]; then
  echo "No graphical session was found. Resource capture requires a real display."
  exit 1
fi

mkdir -p "$(dirname "$output")"
rm -f "$output"

DAISYOS_RESOURCE_REPORT_PATH="$output" \
DAISYOS_RESOURCE_REPORT_DELAY_SECONDS="$delay" \
dotnet run --project "$repo_root/src/DaisyOS.Shell" --no-build -- --skip-onboarding --real-services &
shell_pid=$!
trap 'kill "$shell_pid" 2>/dev/null || true' EXIT

deadline=$((SECONDS + delay + 30))
while [[ ! -s "$output" && $SECONDS -lt $deadline ]]; do
  sleep 1
done

if [[ ! -s "$output" ]]; then
  echo "The shell did not produce a resource report."
  exit 1
fi

kill "$shell_pid" 2>/dev/null || true
wait "$shell_pid" 2>/dev/null || true
trap - EXIT

echo "Resource report written to $output"
