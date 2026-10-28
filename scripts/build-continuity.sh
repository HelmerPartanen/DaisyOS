#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
output="${1:-$repo_root/artifacts/continuity/publish}"
dotnet restore "$repo_root/src/DaisyOS.Continuity/DaisyOS.Continuity.csproj" --locked-mode
dotnet publish "$repo_root/src/DaisyOS.Continuity/DaisyOS.Continuity.csproj" \
  -c Release --no-restore --self-contained false -o "$output"
printf 'Continuity publish: %s\n' "$output"
