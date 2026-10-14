#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
checker="$repo_root/scripts/check-compositor-session.sh"
temp_dir="$(mktemp -d)"
trap 'rm -rf "$temp_dir"' EXIT

mkdir -p "$temp_dir/bin"

cat > "$temp_dir/bin/kwin_wayland" <<'EOF'
#!/usr/bin/env bash
exit 0
EOF
cat > "$temp_dir/bin/Xwayland" <<'EOF'
#!/usr/bin/env bash
exit 0
EOF
cat > "$temp_dir/bin/wayland-info" <<'EOF'
#!/usr/bin/env bash
exit "${DAISYOS_TEST_WAYLAND_INFO_EXIT:-0}"
EOF
cat > "$temp_dir/bin/pgrep" <<'EOF'
#!/usr/bin/env bash
exit "${DAISYOS_TEST_PGREP_EXIT:-0}"
EOF
cat > "$temp_dir/bin/systemctl" <<'EOF'
#!/usr/bin/env bash
exit "${DAISYOS_TEST_SYSTEMCTL_EXIT:-0}"
EOF
chmod +x "$temp_dir/bin/"*

pass_report="$temp_dir/pass.md"
PATH="$temp_dir/bin:$PATH" \
DAISYOS_CHECK_SESSION_TYPE=wayland \
DAISYOS_CHECK_WAYLAND_DISPLAY=wayland-test \
DAISYOS_CHECK_CURRENT_DESKTOP=DaisyOS \
  "$checker" --strict --output "$pass_report"

grep -Fq '| PASS | KWin running |' "$pass_report"
grep -Fq '| PASS | Wayland round trip |' "$pass_report"
grep -Fq '| PASS | XWayland installed |' "$pass_report"
grep -Fq 'Summary: 0 required failure(s), 0 warning(s).' "$pass_report"

failure_report="$temp_dir/failure.md"
if PATH="$temp_dir/bin:$PATH" \
    DAISYOS_CHECK_SESSION_TYPE=x11 \
    DAISYOS_CHECK_WAYLAND_DISPLAY='' \
    DAISYOS_CHECK_CURRENT_DESKTOP=DaisyOS \
    DAISYOS_TEST_PGREP_EXIT=1 \
    DAISYOS_TEST_WAYLAND_INFO_EXIT=1 \
    DAISYOS_TEST_SYSTEMCTL_EXIT=1 \
      "$checker" --strict --output "$failure_report"; then
  echo "Strict compositor validation unexpectedly passed a broken session."
  exit 1
fi

grep -Fq '| FAIL | Wayland session |' "$failure_report"
grep -Fq '| FAIL | Wayland round trip |' "$failure_report"
grep -Fq '| FAIL | KWin running |' "$failure_report"
grep -Fq '| WARN | Desktop portal |' "$failure_report"
grep -Eq 'Summary: [1-9][0-9]* required failure\(s\), 2 warning\(s\).' "$failure_report"

wrong_pid_report="$temp_dir/wrong-pid.md"
if PATH="$temp_dir/bin:$PATH" \
    DAISYOS_CHECK_SESSION_TYPE=wayland \
    DAISYOS_CHECK_WAYLAND_DISPLAY=wayland-test \
    DAISYOS_CHECK_CURRENT_DESKTOP=DaisyOS \
    DAISYOS_CHECK_KWIN_PID="$$" \
      "$checker" --strict --output "$wrong_pid_report"; then
  echo "Strict compositor validation accepted a non-KWin expected process."
  exit 1
fi
grep -Fq '| FAIL | KWin running |' "$wrong_pid_report"

if "$checker" --unknown >/dev/null 2>&1; then
  echo "Unknown compositor-check option unexpectedly succeeded."
  exit 1
fi

echo "Compositor session checker tests passed."
