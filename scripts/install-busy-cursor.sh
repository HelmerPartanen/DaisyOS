#!/usr/bin/env bash
set -Eeuo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
build_dir="$repo_root/src/native/daisy_busy_cursor/build"
"$repo_root/scripts/build-busy-cursor.sh"
# Match the plugin root exported by the native-session supervisor.
plugin_dir="$HOME/.local/lib/qt6/plugins/kwin/effects/plugins"
install -d -m 755 "$plugin_dir"
changed=false
if ! cmp -s "$build_dir/daisyos-busy-cursor-v1.so" "$plugin_dir/daisyos-busy-cursor-v1.so"; then
  changed=true
  # Atomic replacement: never truncate a library currently mapped into KWin.
  install -m 755 "$build_dir/daisyos-busy-cursor-v1.so" "$plugin_dir/daisyos-busy-cursor-v1.so.new"
  mv -f "$plugin_dir/daisyos-busy-cursor-v1.so.new" "$plugin_dir/daisyos-busy-cursor-v1.so"
fi
kwriteconfig6 --file kwinrc --group Plugins --key daisyos-busy-cursor-v1Enabled --type bool true
# Retain stock startup tracking (especially XWayland), replace its icon rendering.
kwriteconfig6 --file kwinrc --group Plugins --key startupfeedbackEnabled --type bool true
kwriteconfig6 --file klaunchrc --group FeedbackStyle --key BusyCursor --type bool false
if qdbus6 org.kde.KWin /Effects org.kde.kwin.Effects.isEffectLoaded daisyos-busy-cursor-v1 2>/dev/null | grep -qx true ||
   qdbus6 org.kde.KWin /Effects org.kde.kwin.Effects.loadEffect daisyos-busy-cursor-v1 2>/dev/null | grep -qx true; then
  qdbus6 org.kde.KWin /Effects org.kde.kwin.Effects.reconfigureEffect startupfeedback >/dev/null
  if [[ "$changed" == true ]]; then
    echo "Native cursor installed. If it was already loaded, updated code takes effect at the next session login."
  fi
else
  echo 'Installed busy cursor; KWin will discover it on the next native session login.'
fi
