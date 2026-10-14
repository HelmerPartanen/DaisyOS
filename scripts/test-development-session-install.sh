#!/usr/bin/env bash
set -Eeuo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
installer="$repo_root/scripts/install-development-session.sh"
launcher="$repo_root/scripts/daisyos-development-session"
temp_dir="$(mktemp -d)"
trap 'rm -rf "$temp_dir"' EXIT

stage_root="$temp_dir/stage"
"$installer" --prefix "$stage_root" --repo "$repo_root"

installed_launcher="$stage_root/usr/local/bin/daisyos-development-session"
installed_entry="$stage_root/usr/share/wayland-sessions/daisyos-development.desktop"
installed_config="$stage_root/etc/daisyos/daisyos-development-session.conf"

[[ -x "$installed_launcher" ]]
[[ -f "$installed_entry" ]]
[[ -f "$installed_config" ]]
grep -Fxq 'Name=DaisyOS (Windowed Prototype)' "$installed_entry"
grep -Fxq 'Exec=/usr/local/bin/daisyos-development-session' "$installed_entry"
grep -Fq 'DAISYOS_DEVELOPMENT_ROOT=' "$installed_config"

if command -v desktop-file-validate >/dev/null 2>&1; then
  desktop-file-validate "$installed_entry"
fi

fake_checkout="$temp_dir/checkout"
mkdir -p "$fake_checkout/src/DaisyOS.Shell" "$temp_dir/bin" "$temp_dir/state"
cat > "$temp_dir/bin/dotnet" <<EOF
#!/usr/bin/env bash
printf '%s\\n' "\$*" > "$temp_dir/dotnet-arguments"
EOF
chmod +x "$temp_dir/bin/dotnet"
printf 'DAISYOS_DEVELOPMENT_ROOT=%q\n' "$fake_checkout" > "$temp_dir/config"

PATH="$temp_dir/bin:$PATH" \
  XDG_STATE_HOME="$temp_dir/state" \
  DAISYOS_DEVELOPMENT_CONFIG="$temp_dir/config" \
  "$launcher" --run-shell

grep -Fq "run --project $fake_checkout/src/DaisyOS.Shell -- --real-services --windowed-prototype" "$temp_dir/dotnet-arguments"

echo "Development-session installer tests passed."
