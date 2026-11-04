#!/usr/bin/env bash
# Disposable shell-control checks. --virtual also requires real compositor callbacks.
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
mode="${1:-}"
if [[ "$mode" == --virtual || "$mode" == --inside-virtual ]]; then
  shift
fi
if [[ $# -ne 1 ]]; then
  echo "Usage: scripts/run-smoothness-acceptance.sh [--virtual] OUTPUT_DIRECTORY" >&2
  exit 2
fi
output="$(realpath -m "$1")"
project="$repo_root/scripts/smoothness-acceptance/SmoothnessAcceptance.csproj"
if [[ "$mode" == --inside-virtual ]]; then
  cd "$repo_root"
  export DAISYOS_ACCEPTANCE_REQUIRE_FRAMES=1
  capture_status=0
  dotnet run --no-build --project "$project" -c Release -- "$output" || capture_status=$?
  if [[ -f "$output/callbacks.jsonl" ]]; then
    python3 "$repo_root/scripts/report-responsiveness.py" "$output/callbacks.jsonl" --refresh-hz 60 --output "$output/report.json"
  fi
  exit "$capture_status"
fi
[[ -f "$repo_root/src/DaisyOS.Shell/bin/Release/net10.0/DaisyOS.Shell.dll" ]] || {
  echo "Build the Release shell first." >&2; exit 2;
}
dotnet build "$project" -c Release --nologo -v quiet
if [[ "$mode" == --virtual ]]; then
  scratch="$(mktemp -d -t daisy-smoothness-XXXXXX)"
  trap 'rm -rf -- "$scratch"' EXIT
  export DAISYOS_ACCEPTANCE_RUNNER="$repo_root/scripts/run-smoothness-acceptance.sh"
  export DAISYOS_ACCEPTANCE_OUTPUT="$output"
  cat > "$scratch/session" <<'SESSION'
#!/usr/bin/env bash
exec bash "$DAISYOS_ACCEPTANCE_RUNNER" --inside-virtual "$DAISYOS_ACCEPTANCE_OUTPUT"
SESSION
  chmod +x "$scratch/session"
  # Separate bus and configuration prevent changes to the running desktop.
  XDG_CONFIG_HOME="$scratch/config" dbus-run-session -- kwin_wayland --virtual \
    --socket "daisy-smoothness-$UID-$$" --width 1280 --height 900 \
    --no-lockscreen --no-global-shortcuts --exit-with-session "$scratch/session"
  # Some KWin versions do not forward the session program's exit status.
  python3 - "$output/acceptance.json" <<'PY'
import json, sys
from pathlib import Path
path = Path(sys.argv[1])
if not path.exists() or not json.loads(path.read_text()).get("passed"):
    raise SystemExit("Acceptance failed; inspect failure.txt and compositor logs")
PY
else
  dotnet run --no-build --project "$project" -c Release -- "$output"
  python3 "$repo_root/scripts/report-responsiveness.py" "$output/callbacks.jsonl" --output "$output/report.json"
fi
