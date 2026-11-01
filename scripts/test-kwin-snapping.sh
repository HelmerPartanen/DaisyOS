#!/usr/bin/env bash
set -Eeuo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
probe="${1:?Usage: test-kwin-snapping.sh /path/to/daisysnaptest.so}"
fixture="$(mktemp -d)"
trap 'rm -rf "$fixture"' EXIT
artifact_dir="$repo_root/artifacts/compositor/snapping"
mkdir -p "$artifact_dir" "$fixture/config" "$fixture/data" "$fixture/plugins/kwin/effects/plugins"
cp "$probe" "$fixture/plugins/kwin/effects/plugins/"
XDG_CONFIG_HOME="$fixture/config" "$repo_root/scripts/configure-kwin-snapping.sh" --no-reconfigure
kwriteconfig6 --file "$fixture/config/kwinrc" --group Plugins --key daisysnaptestEnabled true
# No user compositor, bus, plugin installation or saved tile layouts are touched.
timeout 30 dbus-run-session -- env \
    XDG_CONFIG_HOME="$fixture/config" XDG_DATA_HOME="$fixture/data" \
    QT_PLUGIN_PATH="$fixture/plugins${QT_PLUGIN_PATH:+:$QT_PLUGIN_PATH}" \
    QT_FORCE_STDERR_LOGGING=1 QT_QPA_PLATFORMTHEME=generic XDG_CURRENT_DESKTOP=KDE \
    LIBGL_ALWAYS_SOFTWARE=1 KWIN_COMPOSE=O2 \
    kwin_wayland --virtual --width 1280 --height 720 --no-lockscreen --no-kactivities \
    --socket "daisy-snap-test-$$" \
    --exit-with-session "$repo_root/scripts/test-kwin-snapping-runtime.py" \
    > "$artifact_dir/kwin.log" 2>&1
python3 - "$artifact_dir/kwin.log" <<'PY'
from pathlib import Path
import sys
log = Path(sys.argv[1]).read_text()
assert 'PASS: real KWin geometry' in log, log
assert 'AssertionError' not in log, log
print('PASS: isolated native snapping and tiling; see ' + sys.argv[1])
PY
