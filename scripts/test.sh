#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

if ! command -v dotnet >/dev/null 2>&1; then
  echo "dotnet is missing. Install .NET SDK 10 before running tests."
  exit 1
fi

python3 scripts/localization/verify-catalog.py
python3 scripts/test-daisyos-reliability.py
scripts/test-memory-resilience.sh

dotnet restore
dotnet build --no-restore
dotnet test --no-build
scripts/test-release-scripts.sh
scripts/test-iso-worktree-guard.sh
scripts/check-publish-contract.sh
scripts/check-kwin-prototype.sh
scripts/check-packaging.sh
scripts/test-packaging-check.sh
scripts/check-branding.sh
scripts/test-session-scripts.sh
scripts/test-development-session-install.sh
scripts/test-native-development-session-install.sh
python3 scripts/test-native-shell-recovery.py
scripts/test-compositor-session-check.sh
scripts/check-iso-profile.sh
