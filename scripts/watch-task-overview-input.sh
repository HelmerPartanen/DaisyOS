#!/usr/bin/env bash
set -Eeuo pipefail

echo "DaisyOS overview input trace"
echo "Log: user journal (kwin_wayland)"
echo

python3 - <<'PY'
import dbus

try:
    bus = dbus.SessionBus()
    component = dbus.Interface(
        bus.get_object('org.kde.kglobalaccel', '/component/kwin'),
        'org.kde.kglobalaccel.Component')
    for info in component.allShortcutInfos():
        if 'DaisyOS Task Overview' in ' '.join(map(str, info)):
            print('registered:', ' '.join(map(str, info)))
except Exception as error:
    print('registration query failed:', error)
PY

echo
echo "Press Meta/Super+Tab. Press Ctrl+C when finished."
echo "A working compositor dispatch prints [daisy-overview-input]."
journalctl --user --follow --lines=0 --output=cat \
  | grep --line-buffered -E 'daisy-overview-input|Shortcut|kglobal|task-overview|qml|QML'
