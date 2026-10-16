#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
package_list="$repo_root/os/packages/kwin-wayland-prototype.txt"

required_packages=(
  dbus
  kwin
  plasma-workspace
  xorg-xwayland
  xdg-desktop-portal
  foot
  pipewire
  wireplumber
  wl-clipboard
  wayland-utils
  xterm
)

if [[ ! -f "$package_list" ]]; then
  echo "Missing prototype package list: $package_list"
  exit 1
fi

packages="$(sed '/^[[:space:]]*#/d; /^[[:space:]]*$/d' "$package_list")"
duplicates="$(printf '%s\n' "$packages" | sort | uniq -d)"
if [[ -n "$duplicates" ]]; then
  echo "Duplicate prototype packages:"
  printf '%s\n' "$duplicates"
  exit 1
fi

for package in "${required_packages[@]}"; do
  if ! grep -Fxq "$package" <<<"$packages"; then
    echo "Required prototype package is missing: $package"
    exit 1
  fi
done

if grep -Fxq qt6-wayland <<<"$packages"; then
  echo "qt6-wayland is not a client-shell dependency on current Arch releases; use qt6-base."
  exit 1
fi

if grep -Eq '^[[:space:]]*--fullscreen([[:space:]]|$)' \
  "$repo_root/scripts/run-kwin-prototype.sh"; then
  echo "The nested KWin prototype must remain windowed."
  exit 1
fi

if command -v shellcheck >/dev/null 2>&1; then
  shellcheck \
    "$repo_root/scripts/run-kwin-prototype.sh" \
    "$repo_root/scripts/run-shell-in-kwin.sh" \
    "$repo_root/scripts/check-kwin-prototype.sh" \
    "$repo_root/scripts/check-compositor-session.sh" \
    "$repo_root/scripts/test-compositor-session-check.sh" \
    "$repo_root/scripts/run-kwin-acceptance-session.sh" \
    "$repo_root/scripts/test-kwin-nested-runtime.sh" \
    "$repo_root/scripts/daisyos-native-development-session" \
    "$repo_root/scripts/daisyos-native-development" \
    "$repo_root/scripts/install-native-development-session.sh" \
    "$repo_root/scripts/test-native-development-session-install.sh"
else
  echo "ShellCheck is unavailable; script lint was skipped."
fi

echo "KWin prototype repository checks passed."
