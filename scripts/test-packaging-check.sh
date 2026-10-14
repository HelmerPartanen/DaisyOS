#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
checker="$repo_root/scripts/check-packaging.sh"
temp_dir="$(mktemp -d)"
trap 'rm -rf "$temp_dir"' EXIT

report="$temp_dir/readiness.md"
"$checker" --report "$report"

grep -Fq 'Repository checks: Pass' "$report"
grep -Fq 'Ownership and config preservation: Pass' "$report"
grep -Fq 'Service safety policy: Pass' "$report"
grep -Fq 'Package/database signature policy: Pass' "$report"
grep -Fq 'Release readiness: Blocked' "$report"
grep -Fq 'Choose a redistribution license' "$report"
grep -Fq 'Replace every SKIP source checksum' "$report"
grep -Fq 'Replace the example repository address' "$report"

if "$checker" --release >/dev/null 2>&1; then
  echo "Release validation unexpectedly accepted publication placeholders."
  exit 1
fi

if "$checker" --unknown >/dev/null 2>&1; then
  echo "Unknown packaging-check option unexpectedly succeeded."
  exit 1
fi

echo "Packaging checker tests passed."
