#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
session="$repo_root/os/archiso/DaisyOS/airootfs/usr/local/bin/daisyos-session"
temp_dir="$(mktemp -d)"
trap 'rm -rf "$temp_dir"' EXIT

mkdir -p "$temp_dir/bin" "$temp_dir/state"

cat > "$temp_dir/bin/systemd-cat" <<'EOF'
#!/usr/bin/env bash
cat >/dev/null
EOF
cat > "$temp_dir/bin/fallback" <<EOF
#!/usr/bin/env bash
touch "$temp_dir/fallback-ran"
EOF
cat > "$temp_dir/bin/shell-ok" <<EOF
#!/usr/bin/env bash
printf 'ok\n' >> "$temp_dir/shell-runs"
exit 0
EOF
cat > "$temp_dir/bin/shell-crash" <<EOF
#!/usr/bin/env bash
printf 'crash\n' >> "$temp_dir/shell-runs"
exit 23
EOF
chmod +x "$temp_dir/bin/"*

run_session() {
  PATH="$temp_dir/bin:$PATH" \
  DAISYOS_STATE_DIR="$temp_dir/state" \
  DAISYOS_FALLBACK_COMMAND="$temp_dir/bin/fallback" \
  DAISYOS_RESTART_DELAY_SECONDS=0 \
  DAISYOS_SHELL_BINARY="$1" \
    "$session"
}

run_session "$temp_dir/bin/shell-ok"
[[ "$(wc -l < "$temp_dir/shell-runs")" -eq 1 ]]
[[ ! -e "$temp_dir/fallback-ran" ]]

rm -f "$temp_dir/shell-runs"
run_session "$temp_dir/bin/shell-crash"
[[ "$(wc -l < "$temp_dir/shell-runs")" -eq 3 ]]
[[ -e "$temp_dir/state/use-fallback-session" ]]
[[ -e "$temp_dir/fallback-ran" ]]
grep -Fq 'Crash diagnostics: attempt=1 status=23 signal=none firmware=' "$temp_dir/state/session.log"

rm -f "$temp_dir/fallback-ran" "$temp_dir/shell-runs"
run_session "$temp_dir/bin/shell-ok"
[[ -e "$temp_dir/fallback-ran" ]]
[[ ! -e "$temp_dir/shell-runs" ]]

echo "Session supervisor tests passed."
