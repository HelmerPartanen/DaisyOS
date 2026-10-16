#!/usr/bin/env bash
set -Eeuo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
package_root="$repo_root/packaging/kwin/daisyos-window-bridge"
overview_root="$repo_root/packaging/kwin/daisyos-task-overview"
metadata="$package_root/metadata.json"
script="$package_root/contents/code/main.js"

[[ -f "$metadata" ]]
[[ -f "$script" ]]
grep -Fq '"KPackageStructure": "KWin/Script"' "$metadata"
grep -Fq '"Id": "daisyos-window-bridge"' "$metadata"
grep -Fq 'workspace.windowAdded.connect' "$script"
grep -Fq 'workspace.windowActivated.connect' "$script"
grep -Fq 'window.windowShown.connect' "$script"
grep -Fq 'window.readyForPaintingChanged.connect' "$script"
grep -Fq 'window.desktopFileNameChanged.connect' "$script"
grep -Fq 'window.unresponsiveChanged.connect' "$script"
grep -Fq 'window.skipTaskbarChanged.connect' "$script"
grep -Fq 'workspace.clientList' "$script"
grep -Fq 'window.specialWindow === true' "$script"
grep -Fq 'daisyAppId' "$script"
grep -Fq 'isUnresponsive' "$script"
grep -Fq 'processId' "$script"
grep -Fq 'org.daisyos.notes' "$script"
grep -Fq 'PublishSnapshot' "$script"
grep -Fq 'RequestMediaAction' "$script"
grep -Fq '"Media Play"' "$script"
grep -Fq '"Media Next"' "$script"
grep -Fq '"Media Previous"' "$script"
grep -Fq 'registerShortcut("DaisyOS Capture Region"' "$script"
grep -Fq 'Persistent KWin lifecycle bridge loaded.' "$script"
grep -Fq 'Scripting.unloadScript' "$repo_root/scripts/install-kwin-window-bridge.sh"
grep -Fq 'Scripting.loadScript' "$repo_root/scripts/install-kwin-window-bridge.sh"
grep -Fq '"KPackageStructure": "KWin/Effect"' "$overview_root/metadata.json"
grep -Fq '"Id": "daisyos-task-overview-v15"' "$overview_root/metadata.json"
grep -Fq "dbus.Int32(16777250)" "$repo_root/scripts/install-kwin-window-bridge.sh"
grep -Fq 'sequence: "Meta+Tab"' "$overview_root/contents/ui/main.qml"
grep -Fq 'registerShortcut("DaisyOS Launcher"' "$script"
grep -Fq 'console.warn("[daisy-overview-input] Meta+Tab shortcut activated by KWin' "$overview_root/contents/ui/main.qml"
grep -Fq 'registerShortcut("DaisyOS Gaming Overlay"' "$script"
grep -Fq '"Meta+R"' "$script"
grep -Fq 'requestShellAction("gaming-overlay")' "$script"
grep -Fq 'registerShortcut("DaisyOS Open Files"' "$script"
grep -Fq 'registerShortcut("DaisyOS Open Settings"' "$script"
grep -Fq 'registerShortcut("DaisyOS Notifications"' "$script"
grep -Fq 'registerShortcut("DaisyOS Clipboard History"' "$script"
grep -Fq 'registerShortcut("DaisyOS New Note"' "$script"
grep -Fq 'registerShortcut("DaisyOS Task Manager"' "$script"
grep -Fq '"RequestShellAction"' "$script"
test -x "$repo_root/scripts/watch-task-overview-input.sh"
grep -Fq 'for app_title in Files Settings Notes "Disk Manager"; do' "$repo_root/scripts/install-kwin-window-bridge.sh"
grep -Fq 'WindowRoles.isDaisyWallpaper' "$overview_root/contents/ui/ScreenView.qml"
grep -Fq 'SpringAnimation { spring: 4; damping: 0.5;' "$overview_root/contents/ui/ScreenView.qml"
grep -Fq 'configure-first-party-shortcuts.py' "$repo_root/scripts/install-kwin-window-bridge.sh"
grep -Fq 'import "TransitionGeometry.js" as Transition' "$overview_root/contents/ui/ScreenView.qml"
grep -Fq 'readonly property var transitionRect: Transition.interpolateRect' "$overview_root/contents/ui/ScreenView.qml"
grep -Fq 'property string phase: phaseIdle' "$overview_root/contents/ui/main.qml"
grep -Fq 'pendingActivation = window' "$overview_root/contents/ui/main.qml"
grep -Fq 'sessionReady(root)' "$overview_root/contents/ui/ScreenView.qml"
grep -Fq 'opacity: closing ? 0 : 1' "$overview_root/contents/ui/ScreenView.qml"
if grep -Fq 'opacity: effect.activated ? 1 : 0' "$overview_root/contents/ui/ScreenView.qml"; then
  echo "Task overview cards must never fade out during scene transitions." >&2
  exit 1
fi
if grep -Fq 'ActionPrimary' "$overview_root/contents/ui/ScreenView.qml"; then
  echo "Task overview selection chrome must not use the accent color." >&2
  exit 1
fi
if grep -Fq 'hover.hovered || selected' "$overview_root/contents/ui/ScreenView.qml"; then
  echo "Task overview scale must be driven only by real pointer hover." >&2
  exit 1
fi
if grep -Fq 'anchors.margins: -2' "$overview_root/contents/ui/ScreenView.qml"; then
  echo "Task overview selection must not draw an external white outline." >&2
  exit 1
fi
if grep -Fq 'layer.enabled: true' "$overview_root/contents/ui/ScreenView.qml"; then
  echo "Task overview tiles must keep live thumbnails off intermediate FBO layers." >&2
  exit 1
fi
grep -Fq 'KWinComponents.SceneEffect' "$overview_root/contents/ui/main.qml"
grep -Fq 'DaisyOS Task Overview' "$overview_root/contents/ui/main.qml"
if grep -Fq 'registerShortcut("DaisyOS Task Overview"' "$script"; then
  echo "The persistent bridge must not compete with the compositor effect for overview shortcuts." >&2
  exit 1
fi
grep -Fq 'KWinComponents.WindowThumbnail' "$overview_root/contents/ui/ScreenView.qml"
if grep -Fq 'KWinComponents.WindowFilterModel' "$overview_root/contents/ui/ScreenView.qml"; then
  echo "Task overview must snapshot a plain window model without live filter retargeting." >&2
  exit 1
fi
grep -Fq 'client.closeWindow()' "$overview_root/contents/ui/ScreenView.qml"
if grep -Fq 'case Qt.Key_Meta:' "$overview_root/contents/ui/ScreenView.qml"; then
  echo "The Meta press that opens task overview must not immediately close it again." >&2
  exit 1
fi
grep -Fq 'daisyos-shell' "$overview_root/contents/ui/ScreenView.qml"
grep -Fq 'Bento.layout' "$overview_root/contents/ui/ScreenView.qml"
grep -Fq 'windowviewEnabled false' "$repo_root/scripts/install-kwin-window-bridge.sh"
grep -Fq 'KWin/Effect --install' "$repo_root/scripts/install-kwin-window-bridge.sh"

echo "KWin window bridge and task overview package checks passed."

preview_root="$repo_root/packaging/kwin/daisyos-taskbar-preview"
test -r "$preview_root/metadata.json"
grep -Fq '"X-Plasma-API": "declarativescript"' "$preview_root/metadata.json"
grep -Fq 'KWinComponents.WindowThumbnail' "$preview_root/contents/ui/PreviewCard.qml"
grep -Fq 'Qt.WindowDoesNotAcceptFocus' "$preview_root/contents/ui/main.qml"
grep -Fq 'GetTaskbarPreview' "$preview_root/contents/ui/main.qml"
grep -Fq 'loadDeclarativeScript "$preview_runtime/main.qml"' "$repo_root/scripts/install-kwin-window-bridge.sh"
cmp "$overview_root/contents/ui/WindowRoles.js" "$preview_root/contents/ui/WindowRoles.js"
cmp "$overview_root/contents/ui/RoundedPreviewFrame.qml" "$preview_root/contents/ui/RoundedPreviewFrame.qml"

grep -Fq "KWinComponents.Workspace.windows" "$preview_root/contents/ui/main.qml"
grep -Fq 'wId: card.client ? card.client.internalId' "$preview_root/contents/ui/PreviewCard.qml"
echo "KWin taskbar preview package checks passed."

# Shortcut migration must remove the competing Meta+Tab grab and detect QML load failures.
grep -Fq 'if int(key) != 285212673' "$repo_root/scripts/install-kwin-window-bridge.sh"
grep -Fq 'org.kde.kwin.Effects loadedEffects' "$repo_root/scripts/install-kwin-window-bridge.sh"
if grep -Fq 'sequence: "Meta"' "$overview_root/contents/ui/main.qml"; then
  echo "Bare Meta belongs to Launcher, independently of overview loading." >&2
  exit 1
fi
