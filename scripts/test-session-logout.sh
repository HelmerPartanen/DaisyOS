#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
fixture_root="$(mktemp -d)"
fixture_pid="" unrelated_pid=""
cleanup_fixture() {
  for file in "$fixture_root/shell.pid" "$fixture_root/kwin.pid"; do
    if [[ -f "$file" ]]; then kill -KILL "$(cat "$file")" 2>/dev/null || true; fi
  done
  [[ -z "$fixture_pid" ]] || kill -KILL "$fixture_pid" 2>/dev/null || true
  [[ -z "$unrelated_pid" ]] || kill "$unrelated_pid" 2>/dev/null || true
  rm -rf "$fixture_root"
}
trap cleanup_fixture EXIT
cat > "$fixture_root/supervisor.sh" <<'EOF'
#!/usr/bin/env bash
root="$1"
bash -c 'trap "" TERM; while :; do sleep 0.1; done' &
shell_pid=$!
bash -c 'trap "" TERM; while :; do sleep 0.1; done' &
kwin_pid=$!
printf '%s\n' "$shell_pid" > "$root/shell.pid"
printf '%s\n' "$kwin_pid" > "$root/kwin.pid"
trap 'kill -TERM "$shell_pid"; wait "$shell_pid" || true; kill -TERM "$kwin_pid"; wait "$kwin_pid" || true; exit 0' TERM
touch "$root/ready"
while :; do sleep 0.1; done
EOF
bash "$fixture_root/supervisor.sh" "$fixture_root" &
fixture_pid=$!
for attempt in {1..50}; do [[ ! -f "$fixture_root/ready" ]] || break; sleep 0.1; done
[[ -f "$fixture_root/ready" ]]
sleep 30 &
unrelated_pid=$!
if bash "$repo_root/src/DaisyOS.System/Power/SessionLogout.sh" "$fixture_pid" "$unrelated_pid" "$(cat "$fixture_root/kwin.pid")"; then
  echo "Logout guard accepted a process outside its supervisor." >&2; exit 1
fi
kill -0 "$fixture_pid"
kill -0 "$unrelated_pid"
timeout --kill-after=1 15 bash "$repo_root/src/DaisyOS.System/Power/SessionLogout.sh" "$fixture_pid" \
  "$(cat "$fixture_root/shell.pid")" "$(cat "$fixture_root/kwin.pid")"
for attempt in {1..30}; do if ! kill -0 "$fixture_pid" 2>/dev/null; then break; fi; sleep 0.1; done
! kill -0 "$fixture_pid" 2>/dev/null
wait "$fixture_pid" || true
fixture_pid=""
kill -0 "$unrelated_pid"
echo "Guarded logout stops unresponsive fixture children and preserves unrelated processes."
