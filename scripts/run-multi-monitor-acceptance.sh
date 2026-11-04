#!/usr/bin/env bash
# Never applies test configurations to the current desktop.
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
[[ $# -eq 1 ]] || { echo "Usage: scripts/run-multi-monitor-acceptance.sh OUTPUT_DIRECTORY" >&2; exit 2; }
export DAISYOS_MULTI_MONITOR_OUTPUT="$(realpath -m "$1")"
export DAISYOS_MULTI_MONITOR_ISOLATED=1
export DAISYOS_MULTI_MONITOR_ROOT="$root"
dotnet build "$root/scripts/multi-monitor-acceptance" -c Release --nologo -v quiet
scratch="$(mktemp -d -t daisy-multimonitor-XXXXXX)"
trap 'rm -rf -- "$scratch"' EXIT
cat > "$scratch/session" <<'SESSION'
#!/usr/bin/env bash
cd "$DAISYOS_MULTI_MONITOR_ROOT"
exec dotnet scripts/multi-monitor-acceptance/bin/Release/net10.0/DaisyOS.PerformanceBenchmark.dll "$DAISYOS_MULTI_MONITOR_OUTPUT"
SESSION
chmod +x "$scratch/session"
mkdir -p "$DAISYOS_MULTI_MONITOR_OUTPUT"
rm -f "$DAISYOS_MULTI_MONITOR_OUTPUT/results.json" "$DAISYOS_MULTI_MONITOR_OUTPUT/failure.txt"
QT_QPA_PLATFORM=wayland QT_QPA_PLATFORMTHEME= XDG_CURRENT_DESKTOP=KDE \
XDG_CONFIG_HOME="$scratch/config" XDG_STATE_HOME="$scratch/state" XDG_CACHE_HOME="$scratch/cache" \
  timeout --kill-after=10s 180s dbus-run-session -- kwin_wayland --virtual --output-count 2 \
  --socket "daisy-multimonitor-$UID-$$" --width 1280 --height 900 --no-lockscreen --no-global-shortcuts \
  --exit-with-session "$scratch/session"
python3 - "$DAISYOS_MULTI_MONITOR_OUTPUT/results.json" <<'PY'
import json, sys
from pathlib import Path
path = Path(sys.argv[1])
if not path.exists() or not json.loads(path.read_text()).get("passed"):
    raise SystemExit("Multi-monitor acceptance failed; inspect failure.txt and compositor output")
PY
