#!/usr/bin/env bash
set -Eeuo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
package="$repo_root/packaging/kwin/daisyos-window-motion"
effect_id=daisyos-window-motion
for command in kpackagetool6 kwriteconfig6; do
  command -v "$command" >/dev/null || { echo "$command is required." >&2; exit 1; }
done
# Avoid discovery while KPackage replaces the entry point.
kwriteconfig6 --file kwinrc --group Plugins --key "${effect_id}Enabled" false
if command -v qdbus6 >/dev/null; then
  timeout 3 qdbus6 org.kde.KWin /Effects org.kde.kwin.Effects.unloadEffect "$effect_id" >/dev/null 2>&1 || true
fi
kpackagetool6 --type KWin/Effect --remove "$effect_id" >/dev/null 2>&1 || true
kpackagetool6 --type KWin/Effect --install "$package"
# One owner per transition; retain blur, shadows, frame and overview effects.
for conflict in scale fade glide squash magiclamp minimizeanimation maximize; do
  kwriteconfig6 --file kwinrc --group Plugins --key "${conflict}Enabled" false
  if command -v qdbus6 >/dev/null; then
    timeout 3 qdbus6 org.kde.KWin /Effects org.kde.kwin.Effects.unloadEffect "$conflict" >/dev/null 2>&1 || true
  fi
done
# Older session configurations disabled all KWin animation. The requested
# DaisyOS motion needs a nonzero compositor speed; preserve custom positive speeds.
if command -v kreadconfig6 >/dev/null && [[ "$(kreadconfig6 --file kwinrc --group KDE --key AnimationDurationFactor --default 1)" =~ ^0([.]0+)?$ ]]; then
  kwriteconfig6 --file kwinrc --group KDE --key AnimationDurationFactor 1
fi
kwriteconfig6 --file kwinrc --group Plugins --key "${effect_id}Enabled" true
if command -v qdbus6 >/dev/null && timeout 3 qdbus6 org.kde.KWin /KWin org.kde.KWin.reconfigure >/dev/null 2>&1; then
  timeout 3 qdbus6 org.kde.KWin /Effects org.kde.kwin.Effects.loadEffect "$effect_id"
  timeout 3 qdbus6 org.kde.KWin /Effects org.freedesktop.DBus.Properties.Get org.kde.kwin.Effects loadedEffects | grep -Fxq "$effect_id"
fi
