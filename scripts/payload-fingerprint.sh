#!/usr/bin/env bash
set -euo pipefail

payload_root="${1:-}"
if [[ -z "$payload_root" || ! -d "$payload_root" ]]; then
  echo "Usage: scripts/payload-fingerprint.sh PAYLOAD_DIRECTORY" >&2
  exit 2
fi

payload_root="$(realpath "$payload_root")"
{
  find -P "$payload_root" -mindepth 1 \( -type f -o -type l \) \
      ! -name '.daisyos-build-provenance' -printf '%P\0' \
    | sort -z \
    | while IFS= read -r -d '' relative_path; do
        if [[ -L "$payload_root/$relative_path" ]]; then
          printf 'link\0%s\0%s\0' "$relative_path" "$(readlink "$payload_root/$relative_path")"
        else
          printf 'file\0%s\0' "$relative_path"
          sha256sum "$payload_root/$relative_path" | awk '{print $1}'
        fi
      done
} | sha256sum | awk '{print $1}'
