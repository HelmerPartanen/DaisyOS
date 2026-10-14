#!/usr/bin/env bash
set -euo pipefail

strict=false
output_path=""

usage() {
  cat <<'EOF'
Usage: scripts/check-compositor-session.sh [--strict] [--output PATH]

Checks the current graphical session without changing services or settings.
Use --strict in a KWin test session to fail when a required gate is missing.
EOF
}

while (($# > 0)); do
  case "$1" in
    --strict)
      strict=true
      shift
      ;;
    --output)
      if (($# < 2)); then
        echo "An output path is required after --output."
        exit 2
      fi
      output_path="$2"
      shift 2
      ;;
    --help|-h)
      usage
      exit 0
      ;;
    *)
      echo "Unknown option: $1"
      usage
      exit 2
      ;;
  esac
done

if [[ -n "$output_path" ]]; then
  mkdir -p "$(dirname "$output_path")"
  exec 3>"$output_path"
else
  exec 3>&1
fi

failures=0
warnings=0

report() {
  local status="$1"
  local check="$2"
  local detail="$3"
  printf '| %s | %s | %s |\n' "$status" "$check" "$detail" >&3
  case "$status" in
    FAIL) failures=$((failures + 1)) ;;
    WARN) warnings=$((warnings + 1)) ;;
  esac
}

session_type="${DAISYOS_CHECK_SESSION_TYPE:-${XDG_SESSION_TYPE:-unknown}}"
if [[ -v DAISYOS_CHECK_WAYLAND_DISPLAY ]]; then
  wayland_display="$DAISYOS_CHECK_WAYLAND_DISPLAY"
else
  wayland_display="${WAYLAND_DISPLAY:-}"
fi
current_desktop="${DAISYOS_CHECK_CURRENT_DESKTOP:-${XDG_CURRENT_DESKTOP:-unknown}}"

cat >&3 <<EOF
# DaisyOS compositor session report

Generated: $(date --iso-8601=seconds)

Session type: ${session_type}
Desktop: ${current_desktop}
Wayland display: ${wayland_display:-not set}

| Result | Check | Detail |
|---|---|---|
EOF

if [[ "$session_type" == "wayland" ]]; then
  report PASS "Wayland session" "The session identifies itself as Wayland."
else
  report FAIL "Wayland session" "Sign in to the DaisyOS KWin Wayland test session."
fi

if [[ -n "$wayland_display" ]]; then
  report PASS "Wayland connection" "A Wayland display socket is available."
else
  report FAIL "Wayland connection" "No Wayland display socket is available."
fi

if command -v wayland-info >/dev/null 2>&1 \
    && timeout 5 wayland-info >/dev/null 2>&1; then
  report PASS "Wayland round trip" "The compositor answered a client connection."
else
  report FAIL "Wayland round trip" "The Wayland display did not answer a client connection."
fi

if command -v kwin_wayland >/dev/null 2>&1; then
  report PASS "KWin installed" "The KWin Wayland executable is available."
else
  report FAIL "KWin installed" "Install the reviewed KWin prototype package set."
fi

expected_kwin_pid="${DAISYOS_CHECK_KWIN_PID:-}"
if [[ -n "$expected_kwin_pid" ]] \
    && kill -0 "$expected_kwin_pid" 2>/dev/null \
    && [[ "$(cat "/proc/$expected_kwin_pid/comm" 2>/dev/null)" == "kwin_wayland" ]]; then
  report PASS "KWin running" "The expected nested KWin process is running."
elif [[ -z "$expected_kwin_pid" ]] \
    && command -v pgrep >/dev/null 2>&1 \
    && pgrep -x kwin_wayland >/dev/null 2>&1; then
  report PASS "KWin running" "KWin Wayland is running in this session."
else
  report FAIL "KWin running" "KWin Wayland is not running in this session."
fi

if command -v Xwayland >/dev/null 2>&1; then
  report PASS "XWayland installed" "Legacy X11 applications can be tested."
else
  report FAIL "XWayland installed" "XWayland is missing from the session."
fi

if command -v pgrep >/dev/null 2>&1 && pgrep -f 'DaisyOS[.]Shell|daisyos-shell' >/dev/null 2>&1; then
  report PASS "DaisyOS shell" "The shell process is running."
else
  report FAIL "DaisyOS shell" "The DaisyOS shell process is not running."
fi

if command -v systemctl >/dev/null 2>&1 \
    && systemctl --user is-active --quiet xdg-desktop-portal.service; then
  report PASS "Desktop portal" "Permission-based desktop integration is running."
else
  report WARN "Desktop portal" "Screenshots, sharing, and file dialogs still need portal verification."
fi

if command -v systemctl >/dev/null 2>&1 \
    && systemctl --user is-active --quiet pipewire.service; then
  report PASS "PipeWire" "Audio and portal screen-sharing support is running."
else
  report WARN "PipeWire" "Audio or screen sharing may be unavailable in this session."
fi

cat >&3 <<EOF

Summary: ${failures} required failure(s), ${warnings} warning(s).
EOF

exec 3>&-

if [[ -n "$output_path" ]]; then
  echo "Compositor session report written to $output_path"
fi

if [[ "$strict" == true && "$failures" -gt 0 ]]; then
  exit 1
fi

exit 0
