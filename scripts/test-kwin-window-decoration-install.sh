#!/usr/bin/env bash
set -Eeuo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
installer="$repo_root/scripts/install-kwin-window-decoration.sh"
plugin_source="$repo_root/src/native/daisy_window_decoration"
temp_dir="$(mktemp -d)"
trap 'rm -rf "$temp_dir"' EXIT

[[ -x "$installer" ]]
[[ -f "$plugin_source/daisydecoration.json" ]]
grep -Fq '"Id": "org.daisyos.decoration"' "$plugin_source/daisydecoration.json"
grep -Fq '"styles": ["shadow", "titled"]' "$plugin_source/daisydecoration.json"
grep -Fq 'KDecoration3::Decoration' "$plugin_source/daisydecoration.h"
grep -Fq 'setBorders(QMarginsF(0.0, m_titleBarHeight, 0.0, 0.0))' "$plugin_source/daisydecoration.cpp"
grep -Fq 'Qt::AlignCenter | Qt::TextSingleLine' "$plugin_source/daisydecoration.cpp"
if grep -Fq 'window()->icon().paint' "$plugin_source/daisydecoration.cpp"; then
  echo "The titlebar decoration must not paint application icons." >&2
  exit 1
fi
if grep -Fq 'DaisyFramePainter::paint(painter' "$plugin_source/daisydecoration.cpp"; then
  echo "The titlebar decoration must not paint the compositor-owned window frame." >&2
  exit 1
fi
grep -Fq 'DaisyShadowFactory::get' "$plugin_source/daisydecoration.cpp"
grep -Fq 'style() == KDecoration3::Style::Shadow' "$plugin_source/daisydecoration.cpp"
grep -Fq 'setBorders(QMarginsF(0.0, 0.0, 0.0, 0.0))' "$plugin_source/daisydecoration.cpp"
grep -Fq 'setTitleBar(QRectF())' "$plugin_source/daisydecoration.cpp"
grep -Fq 'ShellFrameOuterEdge' "$plugin_source/daisydecoration.cpp"
grep -Fq 'DecorationShadow must contain only the soft shadow' "$plugin_source/daisyframestyle.cpp"
grep -Fq 'static QHash<QString, std::weak_ptr<KDecoration3::DecorationShadow>> cache' "$plugin_source/daisyframestyle.cpp"
if grep -Fq 'isFilesApp' "$plugin_source/daisydecoration.cpp"; then
  echo "The decoration must not contain application-name special cases." >&2
  exit 1
fi
grep -Fq 'WindowTitleBarHeight' "$repo_root/src/DaisyOS.Shell/Themes/DaisyTheme.axaml"
grep -Fq 'daisyos-window-decorationrc' "$repo_root/src/DaisyOS.Shell/Services/Theming/WindowDecorationThemeService.cs"
grep -Fq '.local/lib/qt6/plugins/org.kde.kdecoration3/org.daisyos.decoration.so' "$repo_root/scripts/daisyos-native-development-session"
grep -Fq '.local/lib/qt6/plugins/org.kde.kdecoration3/org.daisyos.decoration.so' "$repo_root/scripts/daisyos-native-development"

cmake_generator=()
if command -v ninja >/dev/null 2>&1; then
  cmake_generator=(-G Ninja)
fi
cmake -S "$plugin_source" -B "$temp_dir/build" "${cmake_generator[@]}" -DCMAKE_BUILD_TYPE=Debug >/dev/null
cmake --build "$temp_dir/build" >/dev/null
[[ -f "$temp_dir/build/org.daisyos.decoration.so" ]]
grep -a -Fq 'org.daisyos.decoration' "$temp_dir/build/org.daisyos.decoration.so"

"$installer" --prefix "$temp_dir/stage" --debug >/dev/null
[[ -f "$temp_dir/stage/usr/lib/qt6/plugins/org.kde.kdecoration3/org.daisyos.decoration.so" ]]
[[ -f "$temp_dir/stage/etc/daisyos/kwin-window-decoration.conf" ]]
grep -Fxq 'DAISYOS_KWIN_DECORATION_PLUGIN=org.daisyos.decoration' "$temp_dir/stage/etc/daisyos/kwin-window-decoration.conf"

for app_id in settings notes calendar calculator files appstore uikit; do
  [[ -f "$temp_dir/stage/usr/share/applications/org.daisyos.$app_id.desktop" ]]
  [[ -f "$temp_dir/stage/usr/share/icons/hicolor/256x256/apps/org.daisyos.$app_id.png" ]]
  grep -Fxq "Icon=org.daisyos.$app_id" "$temp_dir/stage/usr/share/applications/org.daisyos.$app_id.desktop"
  grep -Fxq "StartupWMClass=org.daisyos.$app_id" "$temp_dir/stage/usr/share/applications/org.daisyos.$app_id.desktop"
done

echo "KWin window-decoration build checks passed."
