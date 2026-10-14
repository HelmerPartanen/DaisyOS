#!/usr/bin/env bash
set -euo pipefail

required_tools=(
  bash
  dotnet
  git
  mkarchiso
  qemu-system-x86_64
  pacman
)

optional_tools=(
  code
  shellcheck
)

missing_required=()
missing_optional=()

for tool in "${required_tools[@]}"; do
  if ! command -v "$tool" >/dev/null 2>&1; then
    missing_required+=("$tool")
  fi
done

for tool in "${optional_tools[@]}"; do
  if ! command -v "$tool" >/dev/null 2>&1; then
    missing_optional+=("$tool")
  fi
done

if ((${#missing_required[@]} == 0)); then
  echo "All required tools are available."
else
  echo "Missing required tools:"
  printf '  - %s\n' "${missing_required[@]}"
fi

if ((${#missing_optional[@]} > 0)); then
  echo "Missing optional tools:"
  printf '  - %s\n' "${missing_optional[@]}"
fi

cat <<'HELP'

This script does not install packages automatically.

On Arch Linux, likely packages include:
  dotnet-sdk
  archiso
  qemu-desktop
  shellcheck
  git
  visual-studio-code-bin or code

Install only after reviewing the package list yourself.
HELP

if ((${#missing_required[@]} > 0)); then
  exit 1
fi

