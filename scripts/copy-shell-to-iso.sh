#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
publish_dir="$repo_root/publish/linux-x64"
publish_mode_file="${publish_dir}.publish-mode"
iso_shell_dir="$repo_root/os/archiso/DaisyOS/airootfs/opt/DaisyOS/shell"

if [[ ! -d "$publish_dir" ]]; then
  echo "Missing publish output at $publish_dir. Run scripts/publish-shell.sh first."
  exit 1
fi

if [[ ! -f "$publish_mode_file" ]] || [[ "$(<"$publish_mode_file")" != "production" ]]; then
  echo "This shell build is for local testing and cannot be copied into an ISO."
  echo "Run scripts/publish-shell.sh without --fast, then try again."
  exit 1
fi

"$repo_root/scripts/check-game-telemetry-payload.sh" "$publish_dir"

for application in Files Mail Notes Settings DiskManager AppInstaller; do
  if [[ ! -x "$publish_dir/apps/$application/DaisyOS.$application" ]]; then
    echo "Standalone $application is missing. Re-run scripts/publish-shell.sh before staging the ISO." >&2
    exit 1
  fi
done

if [[ ! -x "$publish_dir/daisy-launch" ]]; then
  echo "DaisyLaunch is missing. Re-run scripts/publish-shell.sh before staging the ISO." >&2
  exit 1
fi

if [[ ! -f "$publish_dir/daisyos-busy-cursor-v1.so" ]]; then
  echo "Native busy cursor is missing. Re-run scripts/publish-shell.sh before staging the ISO." >&2
  exit 1
fi

[[ -d "$publish_dir/cursor-themes/DaisyOS-Dark/cursors" ]] || { echo "Cursor themes are missing. Publish again." >&2; exit 1; }

mkdir -p "$iso_shell_dir"
find "$iso_shell_dir" -mindepth 1 -maxdepth 1 -exec rm -rf {} +
cp -a "$publish_dir"/. "$iso_shell_dir"/

# Install a normal desktop launcher and on-demand file-manager bus activation.
iso_root="$repo_root/os/archiso/DaisyOS/airootfs"
"$repo_root/scripts/install-memory-resilience.sh" --root "$iso_root" --no-disk-swap --no-package --no-activate
mkdir -p "$iso_root/usr/local/bin" "$iso_root/usr/share/applications" "$iso_root/usr/share/dbus-1/services"
for application in Files Mail Notes Settings DiskManager AppInstaller; do
  lower="${application,,}"
  printf '#!/usr/bin/env bash\nset -euo pipefail\nexec /opt/DaisyOS/shell/apps/%s/DaisyOS.%s "$@"\n' "$application" "$application" > "$iso_root/usr/local/bin/daisyos-$lower"
  chmod +x "$iso_root/usr/local/bin/daisyos-$lower" "$iso_shell_dir/apps/$application/DaisyOS.$application"
  cp "$repo_root/src/native/daisy_window_decoration/applications/org.daisyos.$lower.desktop" "$iso_root/usr/share/applications/"
done
printf '[D-BUS Service]\nName=org.freedesktop.FileManager1\nExec=/opt/DaisyOS/shell/apps/Files/DaisyOS.Files\n' > "$iso_root/usr/share/dbus-1/services/org.freedesktop.FileManager1.service"

# DaisyOS owns desktop consent, file selection and capture UI.
install -m755 "$repo_root/scripts/prepare-screen-sharing.sh" "$iso_root/usr/local/bin/daisyos-prepare-screen-sharing"
install -d "$iso_root/usr/share/xdg-desktop-portal"
install -m644 "$repo_root/packaging/xdg-desktop-portal/daisyos-portals.conf" \
  "$iso_root/usr/share/xdg-desktop-portal/daisyos-portals.conf"
install -Dm644 "$repo_root/packaging/kde/kwalletrc" "$iso_root/etc/xdg/kwalletrc"
install -Dm644 "$repo_root/packaging/kde/kwalletrc" "$iso_root/etc/skel/.config/kwalletrc"
"$repo_root/scripts/stage-desktop-services.sh" --root "$iso_root"
[[ -x "$publish_dir/airpods-root/usr/lib/daisyos/librepods" ]] || { echo "Missing AirPods release payload. Publish again." >&2; exit 1; }
cp -a "$publish_dir/airpods-root/." "$iso_root/"

# Persistent standards-aware launch service and supported one-shot fallback.
mkdir -p "$iso_root/usr/lib/systemd/user/graphical-session.target.wants"
printf '#!/usr/bin/env bash\nexec /opt/DaisyOS/shell/daisy-launch "$@"\n' > "$iso_root/usr/local/bin/daisy-launch"
chmod 755 "$iso_root/usr/local/bin/daisy-launch" "$iso_shell_dir/daisy-launch"
sed 's|^ExecStart=.*|ExecStart=/opt/DaisyOS/shell/daisy-launch|' "$repo_root/packaging/systemd/daisy-launch.service" > "$iso_root/usr/lib/systemd/user/daisy-launch.service"
printf '[D-BUS Service]\nName=org.daisyos.Launcher1\nExec=/opt/DaisyOS/shell/daisy-launch\nSystemdService=daisy-launch.service\n' > "$iso_root/usr/share/dbus-1/services/org.daisyos.Launcher1.service"
ln -sfn ../daisy-launch.service "$iso_root/usr/lib/systemd/user/graphical-session.target.wants/daisy-launch.service"

# Embed the same licensed font in Avalonia and install it for Qt/GTK/Fontconfig.
"$repo_root/scripts/install-system-fonts.sh" --root "$iso_root"

# Native Xcursor themes are consumed by KWin, GTK, Qt and XWayland.
mkdir -p "$iso_root/usr/share/icons" "$iso_root/etc/xdg"
cp -a "$publish_dir/cursor-themes/"DaisyOS-* "$iso_root/usr/share/icons/"
kwriteconfig6 --file "$iso_root/etc/xdg/kcminputrc" --group Mouse --key cursorTheme DaisyOS-Dark
kwriteconfig6 --file "$iso_root/etc/xdg/kcminputrc" --group Mouse --key cursorSize 32
kwriteconfig6 --file "$iso_root/etc/xdg/kcminputrc" --group Touchpad --key ScrollFactor 0.3
kwriteconfig6 --file "$iso_root/etc/xdg/kcminputrc" --group Libinput --group Defaults --group Touchpad --key ScrollFactor 0.3
# New accounts copy the skeleton before KDE's cascading defaults are read.
# Keep its cursor id aligned with the generated Xcursor theme name so KWin can
# load DaisyOS's animated progress/wait frames on the first session.
install -d "$iso_root/etc/skel/.config"
kwriteconfig6 --file "$iso_root/etc/skel/.config/kcminputrc" --group Mouse --key cursorTheme DaisyOS-Dark
kwriteconfig6 --file "$iso_root/etc/skel/.config/kcminputrc" --group Mouse --key cursorSize 32
kwriteconfig6 --file "$iso_root/etc/xdg/kwinrc" --group Plugins --key blurEnabled --type bool true
kwriteconfig6 --file "$iso_root/etc/xdg/kwinrc" --group Effect-blur --key BlurStrength 4

# KWin window menus use the native DaisyOS renderer on every request path.
[[ -f "$publish_dir/window-menus-root/usr/lib/qt6/plugins/kwin/effects/plugins/daisyos-window-menus-v4.so" ]] || { echo 'DaisyOS native window menus are missing. Publish again.' >&2; exit 1; }
install -Dm755 "$publish_dir/window-menus-root/usr/lib/qt6/plugins/kwin/effects/plugins/daisyos-window-menus-v4.so" "$iso_root/usr/lib/qt6/plugins/kwin/effects/plugins/daisyos-window-menus-v4.so"
"$repo_root/scripts/install-window-menu-icons.sh" --root "$iso_root"
kwriteconfig6 --file "$iso_root/etc/xdg/kwinrc" --group Plugins --key daisyos-window-menus-v4Enabled --type bool true

# KWin discovers this native effect through its system Qt plugin directory.
# Build/publish against the same KWin ABI as the target ISO.
install -d "$iso_root/usr/lib/qt6/plugins/kwin/effects/plugins" "$iso_root/etc/xdg"
install -m755 "$publish_dir/daisyos-busy-cursor-v1.so" "$iso_root/usr/lib/qt6/plugins/kwin/effects/plugins/"
kwriteconfig6 --file "$iso_root/etc/xdg/kwinrc" --group Plugins --key daisyos-busy-cursor-v1Enabled --type bool true
kwriteconfig6 --file "$iso_root/etc/xdg/kwinrc" --group Plugins --key startupfeedbackEnabled --type bool true
kwriteconfig6 --file "$iso_root/etc/xdg/klaunchrc" --group FeedbackStyle --key BusyCursor --type bool false
kwriteconfig6 --file "$iso_root/etc/xdg/klaunchrc" --group FeedbackStyle --key Bouncing --type bool false
kwriteconfig6 --file "$iso_root/etc/xdg/klaunchrc" --group FeedbackStyle --key Blinking --type bool false
kwriteconfig6 --file "$iso_root/etc/xdg/klaunchrc" --group FeedbackStyle --key TaskbarButton --type bool false
kwriteconfig6 --file "$iso_root/etc/skel/.config/klaunchrc" --group FeedbackStyle --key BusyCursor --type bool false
kwriteconfig6 --file "$iso_root/etc/skel/.config/klaunchrc" --group FeedbackStyle --key Bouncing --type bool false
kwriteconfig6 --file "$iso_root/etc/skel/.config/klaunchrc" --group FeedbackStyle --key Blinking --type bool false
kwriteconfig6 --file "$iso_root/etc/skel/.config/klaunchrc" --group FeedbackStyle --key TaskbarButton --type bool false

for runtime_executable in DaisyOS.Shell createdump; do
  if [[ -f "$iso_shell_dir/$runtime_executable" ]]; then
    chmod 755 "$iso_shell_dir/$runtime_executable"
  fi
done

source_fingerprint="$("$repo_root/scripts/source-fingerprint.sh")"
source_revision="$(git -C "$repo_root" rev-parse --short=12 HEAD 2>/dev/null || printf 'unknown')"
shell_sha256="$(sha256sum "$iso_shell_dir/DaisyOS.Shell" | awk '{print $1}')"
payload_sha256="$("$repo_root/scripts/payload-fingerprint.sh" "$iso_shell_dir")"
cat > "$iso_shell_dir/.daisyos-build-provenance" <<EOF
source_fingerprint=$source_fingerprint
source_revision=$source_revision
shell_sha256=$shell_sha256
payload_sha256=$payload_sha256
publish_mode=production
EOF

"$repo_root/scripts/stage-gaming-integration.sh" --root "$iso_root"
echo "Copied shell to $iso_shell_dir"
