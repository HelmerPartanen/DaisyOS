#!/usr/bin/env bash
set -Eeuo pipefail
[[ $# == 1 ]] || { echo 'Usage: check-game-telemetry-payload.sh SHELL_DIRECTORY' >&2; exit 2; }
# The shipped shell uses managed/ReadyToRun assemblies, with type names in metadata.
# Check offline: executing an older shell with a new flag could open a desktop.
if [[ ! -f "$1/DaisyOS.Shell.dll" ]] || ! grep -aFq 'GameFrameTelemetryController' "$1/DaisyOS.Shell.dll"; then
  echo 'The shell payload predates automatic FPS capture. Publish and stage the current shell before building the ISO.' >&2
  exit 1
fi
