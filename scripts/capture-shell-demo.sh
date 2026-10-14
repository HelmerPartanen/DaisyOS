#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
artifact_dir="${1:-$repo_root/artifacts/shell-demo/current}"

if ! command -v dotnet >/dev/null 2>&1; then
  echo "dotnet is missing. Install .NET SDK 10 to capture the shell demo."
  exit 1
fi

rm -rf "$artifact_dir"
mkdir -p "$artifact_dir"

demo_test_project="$repo_root/tests/DaisyOS.ShellDemo.Tests/DaisyOS.ShellDemo.Tests.csproj"

dotnet restore "$demo_test_project"
dotnet build "$demo_test_project" --no-restore --maxcpucount:1

DAISYOS_SHELL_DEMO_ARTIFACT_DIR="$artifact_dir" \
  dotnet test "$demo_test_project" \
    --no-build \
    --no-restore \
    --filter 'FullyQualifiedName~ShellDemoHeadlessTests' \
    --logger 'console;verbosity=normal'

expected_frames=(
  desktop.png
  launcher.png
  power-menu.png
  quick-search.png
  calendar.png
  notifications.png
  network-controls.png
  bluetooth-controls.png
  sound-controls.png
  gaming-controls.png
  user-menu.png
  updates.png
  lock-screen.png
  returned-desktop.png
  settings-at-minimum-size.png
  files-at-minimum-size.png
  desktop-at-1024x640.png
  launcher-at-1024x640.png
  desktop-at-1280x800.png
  launcher-at-1280x800.png
  desktop-at-1600x900.png
  launcher-at-1600x900.png
)

for frame in "${expected_frames[@]}"; do
  if [[ ! -s "$artifact_dir/$frame" ]]; then
    echo "Shell demo capture is incomplete: $frame was not created."
    exit 1
  fi
done

echo "Shell demo evidence: $artifact_dir"
echo "Captured ${#expected_frames[@]} validated frames."
