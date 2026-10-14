#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

if ! command -v git >/dev/null 2>&1 || ! command -v sha256sum >/dev/null 2>&1; then
  echo "git and sha256sum are required to fingerprint the DaisyOS source tree." >&2
  exit 1
fi

{
  git -C "$repo_root" ls-files -co --exclude-standard -z -- \
    Directory.Build.props \
    global.json \
    src \
    | sort -z \
    | while IFS= read -r -d '' path; do
        [[ -f "$repo_root/$path" ]] || continue
        printf '%s\0' "$path"
        sha256sum "$repo_root/$path"
      done
} | sha256sum | awk '{print $1}'
