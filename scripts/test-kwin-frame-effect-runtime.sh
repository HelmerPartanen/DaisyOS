#!/usr/bin/env bash
set -Eeuo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
plugin="${1:?Usage: test-kwin-frame-effect-runtime.sh /path/to/daisyframe.so}"
fixture="$(mktemp -d)"
trap 'rm -rf "$fixture"' EXIT
artifact_dir="$repo_root/artifacts/compositor/frame-effect"
mkdir -p "$artifact_dir" "$fixture/config" "$fixture/plugins/kwin/effects/plugins"
cp "$plugin" "$fixture/plugins/kwin/effects/plugins/"
if [[ -n "${2:-}" ]]; then
    mkdir -p "$fixture/plugins/org.kde.kdecoration3"
    cp "$2" "$fixture/plugins/org.kde.kdecoration3/"
fi
cat > "$fixture/config/kwinrc" <<'INI'
[Plugins]
blurEnabled=false
daisyframeEnabled=true
[Round-Corners]
OutlineColor=#ff0000
SecondOutlineColor=#00ff00
ActiveOutlineAlpha=255
ActiveSecondOutlineAlpha=255
INI
if [[ -n "${2:-}" ]]; then
    cat >> "$fixture/config/kwinrc" <<'INI'
[org.kde.kdecoration3]
library=org.daisyos.decoration
INI
fi
timeout 30 dbus-run-session -- env \
    XDG_CONFIG_HOME="$fixture/config" QT_PLUGIN_PATH="$fixture/plugins${QT_PLUGIN_PATH:+:$QT_PLUGIN_PATH}" \
    QT_FORCE_STDERR_LOGGING=1 QT_QPA_PLATFORMTHEME=generic XDG_CURRENT_DESKTOP=KDE LIBGL_ALWAYS_SOFTWARE=1 KWIN_COMPOSE=O2 \
    kwin_wayland --virtual --width 1280 --height 720 --scale "${DAISY_TEST_SCALE:-1}" --no-lockscreen --no-kactivities \
    --socket "daisy-frame-test-$$" \
    --exit-with-session "$repo_root/scripts/test-kwin-frame-effect-runtime.py" \
    > "$artifact_dir/kwin.log" 2>&1
python3 - "$artifact_dir/kwin.log" <<'PY'
from pathlib import Path
import sys
log = Path(sys.argv[1]).read_text()
assert log.count('DaisyFrame: initialized') >= 2, log
assert 'PASS: KWin native outlines' in log, log
assert log.count('DaisyFrame: painting window frames') >= 2, log
for error in ('shader compilation failed', 'Failed to compile', 'GL_INVALID', 'Traceback', 'AssertionError'):
    assert error not in log, log
print('PASS: isolated KWin OpenGL runtime; see ' + sys.argv[1])
PY
