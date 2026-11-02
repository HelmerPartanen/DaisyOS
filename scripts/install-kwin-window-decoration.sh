#!/usr/bin/env bash
set -Eeuo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
source_dir="$repo_root/src/native/daisy_window_decoration"
app_icon_dir="$repo_root/src/DaisyOS.Shell/Assets/AppIcons"
application_dir="$source_dir/applications"
prefix="/"
build_type="Release"
user_install=false

usage() {
  cat <<'EOF'
Usage: scripts/install-kwin-window-decoration.sh [--prefix PATH] [--user] [--debug]

Builds and installs the DaisyOS KDecoration3 plugin. Normal installation also
installs the DaisyOS app-ID desktop metadata and high-quality app icons that
let KWin resolve the correct icon for each server-decorated native window.
It therefore requires administrator privileges.
--user installs the plugin and app metadata below ~/.local without privileges.
--prefix is intended for staging and installer tests.
EOF
}

while (($# > 0)); do
  case "$1" in
    --prefix)
      [[ $# -ge 2 ]] || { echo "--prefix requires a path." >&2; exit 2; }
      prefix="$2"
      shift 2
      ;;
    --debug)
      build_type="Debug"
      shift
      ;;
    --user)
      user_install=true
      shift
      ;;
    --help|-h)
      usage
      exit 0
      ;;
    *)
      echo "Unknown option: $1" >&2
      usage >&2
      exit 2
      ;;
  esac
done

if [[ "$prefix" == "/" && "$user_install" == false && "$EUID" -ne 0 ]]; then
  echo "Administrator privileges are required. Run: sudo $0" >&2
  exit 1
fi

for command in cmake c++; do
  command -v "$command" >/dev/null 2>&1 || {
    echo "Missing build dependency: $command" >&2
    exit 1
  }
done

[[ -f "$source_dir/CMakeLists.txt" ]] || {
  echo "DaisyOS KDecoration source is missing: $source_dir" >&2
  exit 1
}

for required in \
  "$app_icon_dir/Settings.png" \
  "$app_icon_dir/Mail.png" \
  "$app_icon_dir/Notes.png" \
  "$app_icon_dir/Calendar.png" \
  "$app_icon_dir/Calculator.png" \
  "$app_icon_dir/Files.png" \
  "$app_icon_dir/Store.png" \
  "$app_icon_dir/Disk_Manager_Dark.png" \
  "$app_icon_dir/UiKit.png" \
  "$app_icon_dir/AppInstaller.png" \
  "$application_dir/org.daisyos.settings.desktop" \
  "$application_dir/org.daisyos.mail.desktop" \
  "$application_dir/org.daisyos.notes.desktop" \
  "$application_dir/org.daisyos.calendar.desktop" \
  "$application_dir/org.daisyos.calculator.desktop" \
  "$application_dir/org.daisyos.files.desktop" \
  "$application_dir/org.daisyos.appstore.desktop" \
  "$application_dir/org.daisyos.diskmanager.desktop" \
  "$application_dir/org.daisyos.appinstaller.desktop" \
  "$application_dir/org.daisyos.uikit.desktop"; do
  [[ -f "$required" ]] || {
    echo "DaisyOS application icon metadata is missing: $required" >&2
    exit 1
  }
done

plugin_root=""
for candidate in \
  /usr/lib/qt6/plugins/org.kde.kdecoration3 \
  /usr/lib64/qt6/plugins/org.kde.kdecoration3; do
  if [[ -f "$candidate/org.kde.breeze.so" ]]; then
    plugin_root="$candidate"
    break
  fi
done

[[ -n "$plugin_root" ]] || {
  echo "KWin's KDecoration3 plugin directory could not be located." >&2
  exit 1
}

build_dir="$(mktemp -d "${TMPDIR:-/tmp}/daisyos-kdecoration.XXXXXX")"
trap 'rm -rf -- "$build_dir"' EXIT
cmake_generator=()
if command -v ninja >/dev/null 2>&1; then
  cmake_generator=(-G Ninja)
fi
cmake -S "$source_dir" -B "$build_dir" "${cmake_generator[@]}" -DCMAKE_BUILD_TYPE="$build_type"
cmake --build "$build_dir"

plugin_source="$build_dir/org.daisyos.decoration.so"
[[ -f "$plugin_source" ]] || {
  echo "The KDecoration build completed without org.daisyos.decoration.so." >&2
  exit 1
}

if ! grep -a -Fq 'org.daisyos.decoration' "$plugin_source"; then
  echo "The built module is missing its DaisyOS KDecoration metadata." >&2
  exit 1
fi

if [[ "$user_install" == true ]]; then
  install_root="$HOME/.local/lib/qt6/plugins/org.kde.kdecoration3"
  manifest_root="${XDG_CONFIG_HOME:-$HOME/.config}/DaisyOS"
  applications_root="${XDG_DATA_HOME:-$HOME/.local/share}/applications"
  icons_root="${XDG_DATA_HOME:-$HOME/.local/share}/icons/hicolor/256x256/apps"
else
  install_root="$prefix$plugin_root"
  manifest_root="$prefix/etc/daisyos"
  applications_root="$prefix/usr/share/applications"
  icons_root="$prefix/usr/share/icons/hicolor/256x256/apps"
fi
install -d -m 0755 "$install_root" "$manifest_root" "$applications_root" "$icons_root"
install -m 0755 "$plugin_source" "$install_root/org.daisyos.decoration.so"

# KWin receives org.daisyos.<app> as the Wayland app_id. Installing matching
# hicolor icon names and desktop-file IDs lets its DecoratedWindow::icon()
# resolve the exact DaisyOS artwork instead of the generic KWin fallback.
declare -A app_icons=(
  [settings]=Settings.png
  [mail]=Mail.png
  [notes]=Notes.png
  [calendar]=Calendar.png
  [calculator]=Calculator.png
  [files]=Files.png
  [appstore]=Store.png
  [diskmanager]=Disk_Manager_Dark.png
  [appinstaller]=AppInstaller.png
  [uikit]=UiKit.png
)
for app_id in "${!app_icons[@]}"; do
  install -m 0644 "$app_icon_dir/${app_icons[$app_id]}" "$icons_root/org.daisyos.$app_id.png"
  install -m 0644 "$application_dir/org.daisyos.$app_id.desktop" "$applications_root/org.daisyos.$app_id.desktop"
done

if [[ "$prefix" == "/" && "$user_install" == false ]]; then
  command -v gtk-update-icon-cache >/dev/null 2>&1 && gtk-update-icon-cache -f -t /usr/share/icons/hicolor || true
  command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database /usr/share/applications || true
fi

cat > "$manifest_root/kwin-window-decoration.conf" <<EOF
DAISYOS_KWIN_DECORATION_PLUGIN=org.daisyos.decoration
DAISYOS_KWIN_DECORATION_PATH=$install_root/org.daisyos.decoration.so
EOF
chmod 0644 "$manifest_root/kwin-window-decoration.conf"

echo "Installed the DaisyOS KWin decoration to $install_root/org.daisyos.decoration.so"
echo "Installed DaisyOS window icons and desktop metadata to $icons_root and $applications_root"
echo "Sign into DaisyOS (Native Development), or run daisyos-native-development decoration reload and reopen the app."
