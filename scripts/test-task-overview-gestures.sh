#!/usr/bin/env bash
set -Eeuo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
overview_root="$repo_root/packaging/kwin/daisyos-task-overview"
fixture="$(mktemp -d)"
trap 'rm -rf "$fixture"' EXIT
mkdir -p "$fixture/contents/ui" "$fixture/tests"
cp "$overview_root/contents/ui/"*.js "$fixture/contents/ui/"
cp "$overview_root/contents/ui/GlobalGestures.qml" "$fixture/contents/ui/"
cp "$overview_root/tests/state/OverviewGesture.qml.in" "$fixture/tests/tst_OverviewGesture.qml"
# Run the production transition state machine with a fake scene view. Rendering
# WindowThumbnail requires KWin; these tests cover asynchronous readiness and
# real QML settle animation rather than reproducing the transition logic.
python3 - "$overview_root/contents/ui/main.qml" "$fixture/contents/ui/main.qml" <<'PY'
import re
import sys
from pathlib import Path
source = Path(sys.argv[1]).read_text()
source, count = re.subn(r'delegate: ScreenView\s*\{[^}]*\}', 'delegate: Item { }', source)
assert count == 1, 'Expected one ScreenView delegate'
Path(sys.argv[2]).write_text(source)
PY
runner=/usr/lib/qt6/bin/qmltestrunner
[[ -x "$runner" ]] || runner=qmltestrunner
export QT_QPA_PLATFORM=offscreen QT_QUICK_BACKEND=software
"$runner" -input "$fixture/tests" -import "$overview_root/tests/imports"
for suite in FlickMotion GlobalGestures TransitionGeometry; do
    "$runner" -input "$overview_root/tests/tst_${suite}.qml" -import "$overview_root/tests/imports"
done
