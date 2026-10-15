#!/usr/bin/env bash
set -Eeuo pipefail

usage() {
  cat <<'HELP'
Usage: scripts/configure-gaming.sh [--check] [--user USER]

Configure the desktop user's access to the installed GameMode helpers using
GameMode's existing gamemode group and polkit rules. No packages, custom
polkit rules, driver settings, or persistent CPU governors are installed.

Run with sudo to configure; --check is read-only and needs no privileges.
The user defaults to SUDO_USER when configuring, or the current user when checking.
Sign out and back in after changing group membership.
HELP
}

check_only=false
target_user="${SUDO_USER:-}"
while (($#)); do
  case "$1" in
    --check) check_only=true; shift ;;
    --user)
      [[ $# -ge 2 && -n "$2" ]] || { echo '--user requires a username.' >&2; exit 2; }
      target_user="$2"; shift 2 ;;
    --help|-h) usage; exit 0 ;;
    *) usage >&2; exit 2 ;;
  esac
done

if [[ -z "$target_user" && "$check_only" == true ]]; then
  target_user="$(id -un)"
fi
[[ -n "$target_user" && "$target_user" != root ]] || {
  echo 'Choose a desktop user with --user USER (or invoke through sudo).' >&2
  exit 2
}
id -u -- "$target_user" >/dev/null || exit 2
getent group gamemode >/dev/null || {
  echo 'The installed GameMode group is missing. Install the gaming package profile first.' >&2
  exit 1
}
command -v gamemoderun >/dev/null || {
  echo 'GameMode is not installed. Install the gaming package profile first.' >&2
  exit 1
}

in_gaming_group=false
for group in $(id -nG -- "$target_user"); do
  [[ "$group" != gamemode ]] || in_gaming_group=true
done
if [[ "$in_gaming_group" == true ]]; then
  echo "$target_user is configured for the installed GameMode helper policy."
  exit 0
fi
if [[ "$check_only" == true && -f /usr/share/polkit-1/rules.d/60-daisyos-gamemode.rules ]]; then
  echo 'DaisyOS default governor access is configured for active local desktop sessions.'
  echo 'Run gamemoded -t from that desktop session to verify runtime access.'
  exit 0
fi
if [[ "$check_only" == true ]]; then
  echo "$target_user is missing GameMode helper access (gamemode group)."
  exit 1
fi
[[ "$EUID" -eq 0 ]] || { echo 'Run this command with sudo to configure GameMode helper access.' >&2; exit 1; }
usermod --append --groups gamemode -- "$target_user"
echo "Configured GameMode helper access for $target_user. Sign out and back in before testing Gaming Mode."
