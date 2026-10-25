#!/usr/bin/env bash
set -Eeuo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
installer="$repo_root/scripts/install-native-development-session.sh"
session_launcher="$repo_root/scripts/daisyos-native-development-session"
control="$repo_root/scripts/daisyos-native-development"
memory_policy="$repo_root/scripts/daisyos-memory-policy.sh"
temp_dir="$(mktemp -d)"
trap 'rm -rf "$temp_dir"' EXIT

stage_root="$temp_dir/stage"
"$installer" --prefix "$stage_root" --repo "$repo_root"

installed_session="$stage_root/usr/local/bin/daisyos-native-development-session"
installed_control="$stage_root/usr/local/bin/daisyos-native-development"
installed_entry="$stage_root/usr/share/wayland-sessions/daisyos-native-development.desktop"
installed_config="$stage_root/etc/daisyos/daisyos-native-development.conf"

[[ -x "$installed_session" ]]
[[ -s "$installed_session" ]]
[[ -x "$installed_control" ]]
[[ -s "$installed_control" ]]
[[ -f "$installed_entry" ]]
[[ -s "$installed_entry" ]]
[[ -f "$installed_config" ]]
cmp "$repo_root/packaging/xdg-desktop-portal/daisyos-desktop-services.service" \
  "$stage_root/usr/lib/systemd/user/daisyos-desktop-services.service"
grep -Fxq 'Name=DaisyOS (Native Development)' "$installed_entry"
grep -Fxq 'Exec=/usr/local/bin/daisyos-native-development-session' "$installed_entry"
grep -Fq 'DAISYOS_NATIVE_DEVELOPMENT_ROOT=' "$installed_config"
grep -Fq 'DAISYOS_SHELL_SESSION=1' "$session_launcher"
grep -Fq -- '--drm --xwayland' "$session_launcher"
grep -Fq -- '--socket' "$session_launcher"
grep -Fq 'restart.request' "$session_launcher"
grep -Fq 'overviewEnabled false' "$session_launcher"
grep -Fq 'windowviewEnabled false' "$session_launcher"
grep -Fq 'daisyos-task-overview-v15Enabled true' "$session_launcher"
grep -Fq "'DaisyOS Task Overview' 'Meta+Tab,Meta+Tab" "$session_launcher"
grep -Fq 'QT_PLUGIN_PATH' "$session_launcher"
grep -Fq 'install-kwin-frame-effect.sh' "$session_launcher"
grep -Fq 'kwin4_effect_shapecornersEnabled true' "$session_launcher"
grep -Fq 'DisableOutlineMaximize false' "$session_launcher"
grep -Fq "'Show Desktop' 'none,none,Peek at Desktop'" "$session_launcher"
grep -Fq 'dotnet build' "$control"
grep -Fq 'configure_window_decoration' "$session_launcher"
grep -Fq 'decoration)' "$control"
grep -Fq 'kbuildsycoca6 --noincremental' "$control"
# shellcheck disable=SC2016 # These match literal shell fragments in the launcher.
grep -Fq 'MemoryLow=${memory_reserve_mib}M' "$session_launcher"
# shellcheck disable=SC2016 # These match literal shell fragments in the launcher.
grep -Fq 'MemoryHigh=${memory_apps_high_mib}M' "$session_launcher"
grep -Fq 'ManagedOOMMemoryPressure=kill' "$session_launcher"
grep -Fq 'DAISYOS_MEMORY_APP_SLICE=daisyos-apps.slice' "$session_launcher"
grep -Fq 'memory status' "$control"
grep -Fq -- '--add-port=53317/tcp' "$installer"
grep -Fq -- '--add-port=53317/udp' "$installer"
[[ -x "$memory_policy" ]]
grep -Fq 'prepare-screen-sharing.sh' "$session_launcher"
grep -Fxq 'org.freedesktop.impl.portal.FileChooser=daisyos' \
  "$stage_root/usr/share/xdg-desktop-portal/daisyos-portals.conf"
grep -Fxq 'Enabled=false' "$stage_root/etc/xdg/kwalletrc"
grep -Fxq 'apiEnabled=false' "$stage_root/etc/xdg/kwalletrc"
! grep -Fq 'org.freedesktop.impl.portal.Secret=' \
  "$stage_root/usr/share/xdg-desktop-portal/daisyos-portals.conf"

if command -v desktop-file-validate >/dev/null 2>&1; then
  desktop-file-validate "$installed_entry"
fi

fake_checkout="$temp_dir/checkout"
mkdir -p "$fake_checkout/src/DaisyOS.Shell" "$fake_checkout/scripts" "$temp_dir/bin" "$temp_dir/runtime/daisyos-native-development"
cat > "$fake_checkout/scripts/install-kwin-window-bridge.sh" <<'EOF'
#!/usr/bin/env bash
exit 0
EOF
chmod +x "$fake_checkout/scripts/install-kwin-window-bridge.sh"
cat > "$fake_checkout/scripts/install-system-apps.sh" <<'EOF'
#!/usr/bin/env bash
exit "${DAISYOS_TEST_INSTALL_FAILURE:-0}"
EOF
chmod +x "$fake_checkout/scripts/install-system-apps.sh"
cat > "$fake_checkout/scripts/build-mpv-wallpaper.sh" <<'EOF_FIXTURE'
#!/usr/bin/env bash
exit 0
EOF_FIXTURE
chmod +x "$fake_checkout/scripts/build-mpv-wallpaper.sh"
cat > "$temp_dir/bin/dotnet" <<EOF
#!/usr/bin/env bash
printf '%s\\n' "\$*" >> "$temp_dir/dotnet-arguments"
if [[ "\${DAISYOS_TEST_BUILD_FAILURE:-0}" == "1" ]]; then
  exit 1
fi
EOF
chmod +x "$temp_dir/bin/dotnet"
printf 'DAISYOS_NATIVE_DEVELOPMENT_ROOT=%q\n' "$fake_checkout" > "$temp_dir/config"

# A live pid is required so the controller cannot accidentally target a stale
# state file. The fixture process is deliberately the current user.
sleep 30 &
fixture_pid=$!
trap 'kill "$fixture_pid" 2>/dev/null || true; rm -rf "$temp_dir"' EXIT
printf '%s\n' "$$" > "$temp_dir/runtime/daisyos-native-development/supervisor.pid"
printf '%s\n' "$fixture_pid" > "$temp_dir/runtime/daisyos-native-development/shell.pid"
printf '%s\n' 'daisyos-native-test' > "$temp_dir/runtime/daisyos-native-development/wayland-display"
printf '%s\n' 'running' > "$temp_dir/runtime/daisyos-native-development/last-status"

# A failed build must never interrupt the active development shell.
if PATH="$temp_dir/bin:$PATH" \
  XDG_RUNTIME_DIR="$temp_dir/runtime" \
  DAISYOS_NATIVE_DEVELOPMENT_CONFIG="$temp_dir/config" \
  DAISYOS_TEST_BUILD_FAILURE=1 \
  "$control" restart; then
  echo "The restart controller accepted a failed build." >&2
  exit 1
fi
kill -0 "$fixture_pid" 2>/dev/null
[[ ! -e "$temp_dir/runtime/daisyos-native-development/restart.request" ]]

# Installing the rebuilt apps must also succeed before the running shell closes.
if PATH="$temp_dir/bin:$PATH" \
  XDG_RUNTIME_DIR="$temp_dir/runtime" \
  DAISYOS_NATIVE_DEVELOPMENT_CONFIG="$temp_dir/config" \
  DAISYOS_TEST_INSTALL_FAILURE=1 \
  "$control" restart; then
  echo "The restart controller accepted a failed app installation." >&2
  exit 1
fi
kill -0 "$fixture_pid" 2>/dev/null
[[ ! -e "$temp_dir/runtime/daisyos-native-development/restart.request" ]]

printf '%s\n' Release > "$temp_dir/runtime/daisyos-native-development/shell.configuration"
PATH="$temp_dir/bin:$PATH" \
  XDG_RUNTIME_DIR="$temp_dir/runtime" \
  DAISYOS_NATIVE_DEVELOPMENT_CONFIG="$temp_dir/config" \
  "$control" restart

for application in Files Notes Settings DiskManager; do
  grep -Fxq "build $fake_checkout/src/DaisyOS.$application/DaisyOS.$application.csproj -c Release --no-restore --disable-build-servers -m:1 -v:minimal" "$temp_dir/dotnet-arguments"
done
grep -Fxq "build $fake_checkout/src/DaisyOS.Shell/DaisyOS.Shell.csproj -c Debug -f net10.0 --no-restore --disable-build-servers -m:1 -v:minimal" "$temp_dir/dotnet-arguments"
grep -Fxq "build $fake_checkout/src/DaisyOS.Shell/DaisyOS.Shell.csproj -c Release -f net10.0 --no-restore --disable-build-servers -m:1 -v:minimal" "$temp_dir/dotnet-arguments"
grep -Fxq "pid=$fixture_pid" "$temp_dir/runtime/daisyos-native-development/restart.request"
if kill -0 "$fixture_pid" 2>/dev/null; then
  echo "The restart controller did not terminate the requested shell process." >&2
  exit 1
fi

echo "Native development-session installer tests passed."
