#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
artifact_dir="${DAISYOS_KWIN_ARTIFACT_DIR:?Set DAISYOS_KWIN_ARTIFACT_DIR for the acceptance session.}"
shell_binary="$repo_root/publish/linux-x64/DaisyOS.Shell"
checker="$repo_root/scripts/check-compositor-session.sh"
shell_log="$artifact_dir/shell.log"
session_report="$artifact_dir/session-report.md"
runtime_report="$artifact_dir/runtime-report.md"
screenshot_log="$artifact_dir/screenshot.log"
screenshot_path="$artifact_dir/shell-desktop.png"
shell_pid=""
xterm_pid=""

cleanup() {
  if [[ -n "$xterm_pid" ]]; then
    kill "$xterm_pid" 2>/dev/null || true
  fi
  if [[ -n "$shell_pid" ]]; then
    kill "$shell_pid" 2>/dev/null || true
  fi
}
trap cleanup EXIT

if [[ ! -x "$shell_binary" ]]; then
  echo "The production shell is missing. Run scripts/publish-shell.sh first." >&2
  exit 1
fi

mkdir -p "$artifact_dir"
export DAISYOS_SHELL_SERVICES=real
export XDG_CURRENT_DESKTOP=DaisyOS
export XDG_SESSION_DESKTOP=DaisyOS
export XDG_SESSION_TYPE=wayland
export DAISYOS_CHECK_KWIN_PID="$PPID"

DAISYOS_SHELL_SESSION=1 "$shell_binary" --real-services --production --shell-session >"$shell_log" 2>&1 &
shell_pid=$!

deadline=$((SECONDS + 90))
while ((SECONDS < deadline)); do
  if ! kill -0 "$shell_pid" 2>/dev/null; then
    echo "The DaisyOS shell stopped before nested-session validation." >&2
    tail -n 40 "$shell_log" >&2 || true
    exit 1
  fi
  if timeout 5 wayland-info >/dev/null 2>&1; then
    break
  fi
  sleep 1
done
if ! timeout 5 wayland-info >/dev/null 2>&1; then
  echo "The nested Wayland display did not become responsive." >&2
  exit 1
fi

xwayland_result="Blocked: xterm is unavailable."
if command -v xterm >/dev/null 2>&1 && [[ -n "${DISPLAY:-}" ]]; then
  xterm -title DaisyOS-XWayland-Acceptance -e sh -c 'sleep 20' >/dev/null 2>&1 &
  xterm_pid=$!
  sleep 2
  if kill -0 "$xterm_pid" 2>/dev/null; then
    xwayland_result="Pass: xterm opened through the nested XWayland display."
  else
    echo "The XWayland acceptance client did not stay open." >&2
    exit 1
  fi
fi

screenshot_result="Not requested. Set DAISYOS_CAPTURE_NESTED_SCREENSHOT=1 to attempt it."
if [[ "${DAISYOS_CAPTURE_NESTED_SCREENSHOT:-0}" == "1" ]] \
    && command -v spectacle >/dev/null 2>&1; then
  if QT_QPA_PLATFORM=wayland timeout 20 spectacle \
      --background \
      --nonotify \
      --new-instance \
      --fullscreen \
      --output "$screenshot_path" \
      >"$screenshot_log" 2>&1 \
      && [[ -s "$screenshot_path" ]]; then
    screenshot_result="Pass: captured the nested DaisyOS desktop."
  else
    screenshot_result="Blocked: this nested compositor does not expose a compatible screenshot interface."
  fi
elif [[ "${DAISYOS_CAPTURE_NESTED_SCREENSHOT:-0}" == "1" ]]; then
  screenshot_result="Blocked: Spectacle is unavailable."
fi

"$checker" --strict --output "$session_report"

native_result="Blocked: foot is not installed on this host."
if command -v foot >/dev/null 2>&1; then
  native_result="Not run: foot is installed, but interactive focus and decoration review remains."
fi

cat >"$runtime_report" <<EOF
# Nested KWin automated runtime report

- Nested KWin PID: $DAISYOS_CHECK_KWIN_PID
- Wayland display: ${WAYLAND_DISPLAY:-not set}
- XWayland display: ${DISPLAY:-not set}
- DaisyOS shell PID: $shell_pid
- Wayland client round trip: Pass
- XWayland client startup: $xwayland_result
- Nested desktop capture: $screenshot_result
- Native test client: $native_result
- Interactive focus, clipboard, window controls, and visual review: Not run
EOF

kill "$xterm_pid" 2>/dev/null || true
xterm_pid=""
kill "$shell_pid" 2>/dev/null || true
wait "$shell_pid" 2>/dev/null || true
shell_pid=""
trap - EXIT
