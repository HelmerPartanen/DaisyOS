#!/usr/bin/env bash
set -Eeuo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
artifact_dir="$repo_root/artifacts/compositor/tiling-preview"
mkdir -p "$artifact_dir"
# Use the Qt 6 runner explicitly on hosts where Qt 5 owns the generic name.
runner=/usr/lib/qt6/bin/qmltestrunner
[[ -x "$runner" ]] || runner=qmltestrunner
QT_QPA_PLATFORM=offscreen QT_QUICK_BACKEND=software "$runner" \
    -input "$repo_root/packaging/kwin/daisyos-tiling-preview/tests" -o "$artifact_dir/qml-tests.txt",txt
cat "$artifact_dir/qml-tests.txt"
if [[ "${1:-}" != --runtime ]]; then exit 0; fi
fixture="$(mktemp -d)"
trap 'rm -rf "$fixture"' EXIT
mkdir -p "$fixture/config" "$fixture/data/kwin-wayland/outline/daisyos"
cp "$repo_root/packaging/kwin/daisyos-tiling-preview/"*.qml "$fixture/data/kwin-wayland/outline/daisyos/"
# Diagnostic instrumentation is restricted to the disposable fixture.
python3 - "$fixture/data/kwin-wayland/outline/daisyos/outline.qml" <<'PY'
import sys
from pathlib import Path
p = Path(sys.argv[1])
s = p.read_text().replace('    Component.onCompleted: synchronize()', '''    Component.onCompleted: { console.warn("[daisy-tiling-test] loaded"); synchronize(); }
    onShowingChanged: console.warn("[daisy-tiling-test] visible=" + showing)
    Timer {
        interval: 25; running: preview.showing; repeat: true
        onTriggered: console.warn("[daisy-tiling-frame] " + JSON.stringify({x:preview.currentRect.x, y:preview.currentRect.y, width:preview.currentRect.width, height:preview.currentRect.height, progress:preview.progress}))
    }''')
p.write_text(s)
PY
cat > "$fixture/config/kwinrc" <<'INI'
[Outline]
QmlPath=kwin-wayland/outline/daisyos/outline.qml
[Plugins]
blurEnabled=false
INI
# A virtual framebuffer, private D-Bus, private config and software rendering
# isolate this check from the user's compositor and open applications.
timeout 30 dbus-run-session -- env \
    XDG_CONFIG_HOME="$fixture/config" XDG_DATA_HOME="$fixture/data" \
    QT_FORCE_STDERR_LOGGING=1 QT_LOGGING_RULES="kwin_scripting.debug=true" \
    KWIN_COMPOSE=Q LIBGL_ALWAYS_SOFTWARE=1 QT_QUICK_BACKEND=software \
    kwin_wayland --virtual --width 1280 --height 720 --no-lockscreen --no-kactivities \
    --socket "daisy-tiling-test-$$" \
    --exit-with-session "$repo_root/scripts/test-kwin-tiling-preview-runtime.py" \
    >"$artifact_dir/kwin.log" 2>&1
python3 - "$artifact_dir/kwin.log" <<'PY'
import json, sys
from pathlib import Path
log = Path(sys.argv[1]).read_text()
assert '[daisy-tiling-test] loaded' in log, log
assert log.count('[daisy-tiling-test] visible=true') == 2, log
assert log.count('[daisy-tiling-test] visible=false') == 2, log
frames = [json.loads(line.split('[daisy-tiling-frame] ', 1)[1]) for line in log.splitlines() if '[daisy-tiling-frame] ' in line]
assert any(0 < f['progress'] < 1 and 0 < f['x'] < 640 for f in frames), frames
assert any(f['progress'] == 1 and f['y'] == 360 and f['height'] == 360 for f in frames), frames
assert any(f['progress'] == 1 and f['width'] == 1280 for f in frames), frames
for error in ('Component failed to load', 'ReferenceError', 'TypeError', 'Cannot assign', 'Failed to initialize QSettings'):
    assert error not in log, log
print('PASS: actual KWin outline loaded, animated across targets, settled, hid and reopened.')
PY
