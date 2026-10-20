#!/usr/bin/env bash
set -Eeuo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
build_dir="$repo_root/src/native/daisy_window_menu/build"
"$repo_root/scripts/build-window-menus.sh"
# Match the plugin root exported by the native-session supervisor.
plugin_dir="$HOME/.local/lib/qt6/plugins/kwin/effects/plugins"
install -d -m 755 "$plugin_dir"
changed=false
if ! cmp -s "$build_dir/daisyos-window-menus-v1.so" "$plugin_dir/daisyos-window-menus-v1.so"; then
  changed=true
  # Atomic replacement: never truncate a library currently mapped into KWin.
  install -m 755 "$build_dir/daisyos-window-menus-v1.so" "$plugin_dir/daisyos-window-menus-v1.so.new"
  mv -f "$plugin_dir/daisyos-window-menus-v1.so.new" "$plugin_dir/daisyos-window-menus-v1.so"
fi
kwriteconfig6 --file kwinrc --group Plugins --key daisyos-window-menus-v1Enabled --type bool true
"$repo_root/scripts/install-window-menu-icons.sh" --user
# The native effect can be loaded without restarting KWin or closing applications.
if qdbus6 org.kde.KWin /Effects org.kde.kwin.Effects.isEffectLoaded daisyos-window-menus-v1 2>/dev/null | grep -qx true; then
  if [[ "$changed" == true ]]; then
    qdbus6 org.kde.KWin /Effects org.kde.kwin.Effects.unloadEffect daisyos-window-menus-v1 >/dev/null
    qdbus6 org.kde.KWin /Effects org.kde.kwin.Effects.loadEffect daisyos-window-menus-v1 >/dev/null
  fi
else
  qdbus6 org.kde.KWin /Effects org.kde.kwin.Effects.loadEffect daisyos-window-menus-v1 >/dev/null 2>&1 || \
    echo 'Installed DaisyOS window menus; KWin will discover them on the next native session login.'
fi
