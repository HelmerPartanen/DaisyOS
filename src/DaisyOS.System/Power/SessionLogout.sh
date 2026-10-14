#!/usr/bin/env bash
set -euo pipefail

# Runs in an independent user service, so shell shutdown cannot kill its guard.
supervisor_pid="${1:-}" shell_pid="${2:-}" compositor_pid="${3:-}"
for process_id in "$supervisor_pid" "$shell_pid" "$compositor_pid"; do
  [[ "$process_id" =~ ^[0-9]+$ ]] && (( process_id > 1 )) || exit 1
  [[ "$(stat -c '%u' "/proc/$process_id" 2>/dev/null || true)" == "$UID" ]] || exit 1
done

is_session_child() {
  local process_id="$1"
  [[ "$(stat -c '%u' "/proc/$process_id" 2>/dev/null || true)" == "$UID" ]] || return 1
  local parent_id
  parent_id="$(awk '/^PPid:/{print $2}' "/proc/$process_id/status" 2>/dev/null || true)"
  [[ "$parent_id" == "$supervisor_pid" ]]
}

is_session_child "$shell_pid" && is_session_child "$compositor_pid" || exit 1
kill -TERM "$supervisor_pid"

# Give the supervisor its normal ordered shutdown, then unblock older launchers
# whose cleanup waits indefinitely for a child that ignores SIGTERM.
deadline=$((SECONDS + 3))
while kill -0 "$supervisor_pid" 2>/dev/null && (( SECONDS < deadline )); do sleep 0.1; done
for process_id in "$shell_pid" "$compositor_pid"; do
  if is_session_child "$process_id"; then
    kill -TERM "$process_id" 2>/dev/null || true
    deadline=$((SECONDS + 3))
    while is_session_child "$process_id" && (( SECONDS < deadline )); do sleep 0.1; done
    if is_session_child "$process_id"; then kill -KILL "$process_id" 2>/dev/null || true; fi
  fi
done
