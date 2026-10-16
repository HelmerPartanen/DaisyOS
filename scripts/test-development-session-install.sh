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
cmp "$repo_root/packaging/xdg-desktop-portal/daisyos-desktop-services.service" \
  "$stage_root/usr/lib/systemd/user/daisyos-desktop-services.service"
grep -Fxq 'Name=DaisyOS (Windowed Prototype)' "$installed_entry"
grep -Fxq 'Exec=/usr/local/bin/daisyos-development-session' "$installed_entry"
grep -Fq 'DAISYOS_DEVELOPMENT_ROOT=' "$installed_config"

if command -v desktop-file-validate >/dev/null 2>&1; then
  desktop-file-validate "$installed_entry"
fi

fake_checkout="$temp_dir/checkout"
mkdir -p "$fake_checkout/src/DaisyOS.Shell" "$fake_checkout/scripts" "$temp_dir/bin" "$temp_dir/state"
cat > "$fake_checkout/scripts/prepare-screen-sharing.sh" <<EOF
#!/usr/bin/env bash
touch "$temp_dir/capture-prepared"
EOF
chmod +x "$fake_checkout/scripts/prepare-screen-sharing.sh"
for tool in systemctl dbus-update-activation-environment; do
  printf '#!/usr/bin/env bash\nexit 0\n' > "$temp_dir/bin/$tool"
  chmod +x "$temp_dir/bin/$tool"
done
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

[[ -e "$temp_dir/capture-prepared" ]]
grep -Fxq 'org.freedesktop.impl.portal.FileChooser=daisyos' "$stage_root/usr/share/xdg-desktop-portal/daisyos-portals.conf"
grep -Fq "run --project $fake_checkout/src/DaisyOS.Shell -- --real-services --windowed-prototype" "$temp_dir/dotnet-arguments"

echo "Development-session installer tests passed."
