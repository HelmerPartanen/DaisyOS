#!/usr/bin/env bash
set -Eeuo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
package_root="$repo_root/packaging/kwin/daisyos-window-bridge"
overview_package_root="$repo_root/packaging/kwin/daisyos-task-overview"
preview_package_root="$repo_root/packaging/kwin/daisyos-taskbar-preview"
preview_id="daisyos-taskbar-preview"
installed_preview="$HOME/.local/share/kwin/scripts/$preview_id/contents/ui/main.qml"
bridge_id="daisyos-window-bridge"
overview_id="daisyos-task-overview-v15"
legacy_overview_ids=("daisyos-task-overview" "daisyos-task-overview-v2" "daisyos-task-overview-v3" "daisyos-task-overview-v4" "daisyos-task-overview-v5" "daisyos-task-overview-v6" "daisyos-task-overview-v7" "daisyos-task-overview-v8" "daisyos-task-overview-v9" "daisyos-task-overview-v10" "daisyos-task-overview-v11" "daisyos-task-overview-v12" "daisyos-task-overview-v13" "daisyos-task-overview-v14")
installed_script="$HOME/.local/share/kwin/scripts/$bridge_id/contents/code/main.js"
installed_overview="$HOME/.local/share/kwin/effects/$overview_id/contents/ui/main.qml"

if ! command -v kpackagetool6 >/dev/null 2>&1; then
  echo "kpackagetool6 is required to install the DaisyOS KWin bridge." >&2
  exit 1
fi

if [[ ! -f "$package_root/metadata.json" || ! -f "$package_root/contents/code/main.js" ]]; then
  echo "DaisyOS KWin bridge package is incomplete: $package_root" >&2
  exit 1
fi

if [[ ! -f "$overview_package_root/metadata.json" || ! -f "$overview_package_root/contents/ui/main.qml" ]]; then
  echo "DaisyOS task overview package is incomplete: $overview_package_root" >&2
  exit 1
fi

if [[ ! -f "$preview_package_root/metadata.json" || ! -f "$preview_package_root/contents/ui/main.qml" ]]; then
  echo "DaisyOS taskbar preview package is incomplete: $preview_package_root" >&2
  exit 1
fi
if command -v qdbus6 >/dev/null 2>&1; then
  qdbus6 org.kde.KWin /Scripting org.kde.kwin.Scripting.unloadScript "$preview_id" >/dev/null 2>&1 || true
fi
kpackagetool6 --type KWin/Script --remove "$preview_id" >/dev/null 2>&1 || true
kpackagetool6 --type KWin/Script --install "$preview_package_root"

kpackagetool6 --type KWin/Script --remove "$bridge_id" >/dev/null 2>&1 || true
kpackagetool6 --type KWin/Script --install "$package_root"
if command -v qdbus6 >/dev/null 2>&1; then
  # Unload before replacing the package. Otherwise KWin keeps the old QML
  # instance and its stale shortcut registrations alive until logout.
  qdbus6 org.kde.KWin /Effects org.kde.kwin.Effects.unloadEffect "$overview_id" >/dev/null 2>&1 || true
fi
for legacy_overview_id in "${legacy_overview_ids[@]}"; do
  kpackagetool6 --type KWin/Effect --remove "$legacy_overview_id" >/dev/null 2>&1 || true
done
# KWin can discover an enabled effect while KPackage is copying its files.
# Disable automatic loading during replacement, and retain an identical
# package on login so its QML imports never temporarily disappear.
if ! diff -qr "$overview_package_root" "$(dirname "$(dirname "$(dirname "$installed_overview")")")" >/dev/null 2>&1; then
  if command -v kwriteconfig6 >/dev/null 2>&1; then
    kwriteconfig6 --file kwinrc --group Plugins --key "${overview_id}Enabled" false
  fi
  if command -v qdbus6 >/dev/null 2>&1; then
    qdbus6 org.kde.KWin /KWin org.kde.KWin.reconfigure >/dev/null 2>&1 || true
  fi
  kpackagetool6 --type KWin/Effect --remove "$overview_id" >/dev/null 2>&1 || true
  kpackagetool6 --type KWin/Effect --install "$overview_package_root"
fi

if [[ ! -f "$installed_script" ]]; then
  echo "DaisyOS KWin bridge was installed without its script: $installed_script" >&2
  exit 1
fi

if [[ ! -f "$installed_overview" ]]; then
  echo "DaisyOS task overview was installed without its QML entry point: $installed_overview" >&2
  exit 1
fi
for overview_dependency in GlobalGestures.qml ScreenView.qml RoundedPreviewFrame.qml BentoLayout.js TransitionGeometry.js WindowRoles.js; do
  if [[ ! -r "$(dirname "$installed_overview")/$overview_dependency" ]]; then
    echo "DaisyOS task overview is missing required installed file: $overview_dependency" >&2
    exit 1
  fi
done

if command -v kwriteconfig6 >/dev/null 2>&1; then
  kwriteconfig6 --file kwinrc --group Plugins --key "${bridge_id}Enabled" true
  kwriteconfig6 --file kwinrc --group Plugins --key "${preview_id}Enabled" true
  kwriteconfig6 --file kwinrc --group Plugins --key "${overview_id}Enabled" true
  kwriteconfig6 --file kwinrc --group Plugins --key overviewEnabled false
  kwriteconfig6 --file kwinrc --group Plugins --key windowviewEnabled false
  kwriteconfig6 --file kwinrc --group Plugins --key blurEnabled true
  kwriteconfig6 --file kwinrc --group Effect-blur --key BlurStrength 4
  kwriteconfig6 --file kglobalshortcutsrc --group plasmashell --key 'activate application launcher' 'none,none,Activate Application Launcher'
  kwriteconfig6 --file kglobalshortcutsrc --group kwin --key 'DaisyOS Task Overview' 'Meta+Tab,Meta+Tab,Toggle DaisyOS Task Overview'
  kwriteconfig6 --file kglobalshortcutsrc --group kwin --key 'DaisyOS Task Overview Alt' 'none,none,Toggle DaisyOS Task Overview'
  kwriteconfig6 --file kglobalshortcutsrc --group kwin --key 'DaisyOS Launcher' 'Meta,Meta,Toggle DaisyOS Launcher'
  kwriteconfig6 --file kglobalshortcutsrc --group kwin --key 'DaisyOS Gaming Overlay' 'Meta+R,Meta+R,Toggle DaisyOS gaming overlay'
  kwriteconfig6 --file kwinrc --group ModifierOnlyShortcuts --key Meta ''

  # Custom app frames must be borderless from their first map, including the
  # shared process identity before each app-specific Wayland identity arrives.
  existing_rules="$(kreadconfig6 --file kwinrulesrc --group General --key rules 2>/dev/null || true)"
  for app_title in Files Settings Notes "Disk Manager"; do
    app_id="${app_title,,}"
    app_id="${app_id// /}"
    rule_id="daisyos-${app_id}-initial-noborder"
    if [[ ",$existing_rules," != *",$rule_id,"* ]]; then
      existing_rules="${existing_rules:+$existing_rules,}$rule_id"
    fi
    kwriteconfig6 --file kwinrulesrc --group "$rule_id" --key Description "DaisyOS $app_title initial client decorations"
    kwriteconfig6 --file kwinrulesrc --group "$rule_id" --key title "$app_title"
    kwriteconfig6 --file kwinrulesrc --group "$rule_id" --key titlematch 1
    kwriteconfig6 --file kwinrulesrc --group "$rule_id" --key wmclass "^(daisyos\.shell|org\.daisyos\.$app_id)$"
    kwriteconfig6 --file kwinrulesrc --group "$rule_id" --key wmclasscomplete false
    kwriteconfig6 --file kwinrulesrc --group "$rule_id" --key wmclassmatch 3
    kwriteconfig6 --file kwinrulesrc --group "$rule_id" --key noborder true
    kwriteconfig6 --file kwinrulesrc --group "$rule_id" --key noborderrule 2
  done
  rule_count="$(awk -F, '{ print NF }' <<< "$existing_rules")"
  kwriteconfig6 --file kwinrulesrc --group General --key rules "$existing_rules"
  kwriteconfig6 --file kwinrulesrc --group General --key count "$rule_count"
fi

# Migrate live grabs as well as disk configuration; preserve other switcher bindings.
python3 -c "
import dbus
try:
    bus = dbus.SessionBus()
    accel = dbus.Interface(bus.get_object('org.kde.kglobalaccel', '/kglobalaccel'), 'org.kde.KGlobalAccel')
    # Meta+R belongs to the HUD; retain Spectacle's other recording binding.
    recorder = ['org.kde.spectacle.desktop', 'RecordRegion', 'default', '']
    recorder_keys = [key for key in accel.shortcut(recorder) if int(key) != 268435538]
    accel.setForeignShortcut(recorder, dbus.Array(recorder_keys, signature='i'))
    accel.setForeignShortcut(['kwin', 'DaisyOS Gaming Overlay', 'default', ''], dbus.Array([dbus.Int32(268435538)], signature='i'))
    accel.setForeignShortcut(['plasmashell', 'activate application launcher', 'default', ''], dbus.Array([], signature='i'))
    # KWin 6.7 no longer consumes ModifierOnlyShortcuts. KGlobalAccel does
    # support a modifier-only QKeySequence, so register bare Meta there.
    switcher = ['kwin', 'Walk Through Windows', 'default', '']
    keys = [key for key in accel.shortcut(switcher) if int(key) != 285212673]
    accel.setForeignShortcut(switcher, dbus.Array(keys, signature='i'))
    accel.setForeignShortcut(['kwin', 'DaisyOS Task Overview', 'default', ''], dbus.Array([dbus.Int32(285212673)], signature='i'))
    accel.setForeignShortcut(['kwin', 'DaisyOS Launcher', 'default', ''], dbus.Array([dbus.Int32(16777250)], signature='i'))
    accel.setForeignShortcut(['kwin', 'DaisyOS Task Overview Alt', 'default', ''], dbus.Array([], signature='i'))
    accel.setForeignShortcut(['kwin', 'DaisyOS Compositor Task Overview', 'default', ''], dbus.Array([], signature='i'))
except Exception as error:
    print(f'DaisyOS live shortcut migration failed: {error}', file=__import__('sys').stderr)
" || true

# Package installation alone only makes the script discoverable on KWin's next
# startup. Native Development installs after KWin is already running, so load
# this exact script explicitly. Without this, initial snapshots from temporary
# command scripts work, but no long-lived bridge observes added/removed/state
# events until the compositor itself is restarted.
if command -v qdbus6 >/dev/null 2>&1; then
  for legacy_overview_id in "${legacy_overview_ids[@]}"; do
    if command -v kwriteconfig6 >/dev/null 2>&1; then
      kwriteconfig6 --file kwinrc --group Plugins --key "${legacy_overview_id}Enabled" false
    fi
    qdbus6 org.kde.KWin /Effects org.kde.kwin.Effects.unloadEffect "$legacy_overview_id" >/dev/null 2>&1 || true
  done
  qdbus6 org.kde.KWin /Scripting org.kde.kwin.Scripting.unloadScript "$bridge_id" >/dev/null 2>&1 || true
  qdbus6 org.kde.KWin /KWin org.kde.KWin.reconfigure >/dev/null 2>&1 || true
  qdbus6 org.kde.KWin /Scripting org.kde.kwin.Scripting.loadScript "$installed_script" "$bridge_id" >/dev/null
  qdbus6 org.kde.KWin /Scripting org.kde.kwin.Scripting.start >/dev/null
  # QQmlEngine caches components by URL across script reloads. Give changed
  # preview code a content-specific URL so development restarts load it live.
  preview_hash="$(sha256sum "$(dirname "$installed_preview")"/* | sha256sum | cut -d ' ' -f1)"
  preview_runtime="${XDG_RUNTIME_DIR:-$HOME/.cache}/daisyos/kwin-taskbar-preview/$preview_hash"
  (umask 077; mkdir -p "$preview_runtime")
  cp "$(dirname "$installed_preview")"/* "$preview_runtime/"
  # Reconfigure can auto-start the installed package. Replace that instance
  # before loading the content-specific URL, or it keeps cached QML alive.
  qdbus6 org.kde.KWin /Scripting org.kde.kwin.Scripting.unloadScript "$preview_id" >/dev/null 2>&1 || true
  qdbus6 org.kde.KWin /Scripting org.kde.kwin.Scripting.loadDeclarativeScript "$preview_runtime/main.qml" "$preview_id" >/dev/null
  qdbus6 org.kde.KWin /Scripting org.kde.kwin.Scripting.start >/dev/null
  qdbus6 org.kde.KWin /Effects org.kde.kwin.Effects.loadEffect "$overview_id" >/dev/null
  if ! qdbus6 org.kde.KWin /Effects org.freedesktop.DBus.Properties.Get org.kde.kwin.Effects loadedEffects | grep -Fxq "$overview_id"; then
    echo "DaisyOS task overview failed to load; inspect the KWin journal for QML errors." >&2
    exit 1
  fi
fi

/usr/bin/python3 "$repo_root/scripts/configure-first-party-shortcuts.py"

"$repo_root/scripts/install-kwin-window-motion.sh"
"$repo_root/scripts/install-kwin-tiling-preview.sh"

echo "Installed DaisyOS KWin window bridge and compositor task overview and live taskbar previews for this user session."
