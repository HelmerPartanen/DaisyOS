#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
# Generated C# from protoc 36.1; runtime pinned at 3.36.2 in the daemon project.
if [[ "$(protoc --version)" != 'libprotoc 36.1' ]]; then
  printf 'Use protoc 36.1 to reproduce the checked-in bindings.\n' >&2
  exit 1
fi
protoc --proto_path="$repo_root/protocol" --csharp_out="$repo_root/src/DaisyOS.Continuity/Generated" continuity/v1/continuity.proto
