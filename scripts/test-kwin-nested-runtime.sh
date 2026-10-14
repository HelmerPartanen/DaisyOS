#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
artifact_dir="${DAISYOS_KWIN_ARTIFACT_DIR:-$repo_root/artifacts/compositor/nested-runtime}"
session_runner="$repo_root/scripts/run-kwin-acceptance-session.sh"
kwin_log="$artifact_dir/kwin.log"
kwin_pid=""

cleanup() {
  if [[ -n "$kwin_pid" ]]; then
    kill "$kwin_pid" 2>/dev/null || true
  fi
}
trap cleanup EXIT

for command in kwin_wayland wayland-info timeout; do
  if ! command -v "$command" >/dev/null 2>&1; then
    echo "$command is required for nested KWin runtime validation." >&2
    exit 1
  fi
done
if [[ -z "${WAYLAND_DISPLAY:-}" && -z "${DISPLAY:-}" ]]; then
  echo "Run nested KWin validation from an existing graphical session." >&2
  exit 1
fi

rm -rf "$artifact_dir"
mkdir -p "$artifact_dir"
host_kwin_pids="$(pgrep -x kwin_wayland 2>/dev/null || true)"
socket_name="daisyos-acceptance-$$"
parent_options=()
host_probe=""
if [[ -n "${WAYLAND_DISPLAY:-}" ]]; then
  if ! timeout 5 wayland-info >/dev/null 2>&1; then
    echo "The host Wayland session did not answer before nested validation." >&2
    exit 1
  fi
  host_probe="wayland"
  parent_options+=(--wayland-display "$WAYLAND_DISPLAY")
else
  if ! command -v xdpyinfo >/dev/null 2>&1 \
      || ! timeout 5 xdpyinfo -display "$DISPLAY" >/dev/null 2>&1; then
    echo "The host X11 session did not answer before nested validation." >&2
    exit 1
  fi
  host_probe="x11"
  parent_options+=(--x11-display "$DISPLAY")
fi

printf 'Starting isolated nested KWin runtime validation.\n'
# Nested validation checks session behavior, not host-GPU performance. Default
# to software composition so a driver reset cannot take down the developer's
# real desktop; opt out explicitly when exercising the GPU path.
DAISYOS_KWIN_ARTIFACT_DIR="$artifact_dir" \
LIBGL_ALWAYS_SOFTWARE="${DAISYOS_KWIN_TEST_SOFTWARE_RENDERING:-1}" \
KWIN_COMPOSE="${DAISYOS_KWIN_TEST_COMPOSE:-Q}" \
QT_QUICK_BACKEND="${DAISYOS_KWIN_TEST_QT_QUICK_BACKEND:-software}" \
kwin_wayland \
  "${parent_options[@]}" \
  --socket "$socket_name" \
  --xwayland \
  --locale1 \
  --width 1280 \
  --height 800 \
  --output-count 1 \
  --no-lockscreen \
  --exit-with-session "$session_runner" \
  >"$kwin_log" 2>&1 &
kwin_pid=$!

if ! timeout 180 tail --pid="$kwin_pid" -f /dev/null; then
  echo "Nested KWin validation exceeded 180 seconds." >&2
  tail -n 80 "$kwin_log" >&2 || true
  exit 1
fi
if ! wait "$kwin_pid"; then
  echo "Nested KWin validation failed." >&2
  tail -n 80 "$kwin_log" >&2 || true
  exit 1
fi
kwin_pid=""

for host_pid in $host_kwin_pids; do
  if ! kill -0 "$host_pid" 2>/dev/null; then
    echo "The host KWin process changed during isolated validation." >&2
    exit 1
  fi
done
if [[ "$host_probe" == "wayland" ]] \
    && ! timeout 5 wayland-info >/dev/null 2>&1; then
  echo "The host Wayland session stopped responding after nested validation." >&2
  exit 1
fi
if [[ "$host_probe" == "x11" ]] \
    && ! timeout 5 xdpyinfo -display "$DISPLAY" >/dev/null 2>&1; then
  echo "The host X11 session stopped responding after nested validation." >&2
  exit 1
fi

grep -Fq 'Summary: 0 required failure(s)' "$artifact_dir/session-report.md"
grep -Fq 'Wayland client round trip: Pass' "$artifact_dir/runtime-report.md"
grep -Fq 'XWayland client startup: Pass' "$artifact_dir/runtime-report.md"

cat >"$artifact_dir/host-isolation-report.md" <<EOF
# Host isolation report

- Parent session probe: $host_probe
- Parent session before nested KWin: Pass
- Parent session after nested KWin: Pass
- Pre-existing host KWin processes preserved: ${host_kwin_pids:-Not applicable}
EOF

printf 'Nested KWin runtime validation passed. Evidence: %s\n' "$artifact_dir"
