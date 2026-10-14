#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

if command -v dotnet >/dev/null 2>&1; then
  dotnet format --verify-no-changes
else
  echo "Skipping dotnet format: dotnet is missing."
fi

if command -v shellcheck >/dev/null 2>&1; then
  shellcheck scripts/*.sh
else
  echo "Skipping ShellCheck: shellcheck is missing."
fi

