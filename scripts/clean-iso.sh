#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "$repo_root/scripts/iso-worktree-guard.sh"
work_dir="$repo_root/work/archiso"

daisyos_assert_safe_iso_work_dir "$repo_root" "$work_dir"

if [[ "${EUID:-$(id -u)}" -ne 0 ]] && daisyos_iso_work_tree_has_mounts "$work_dir"; then
  echo "The ISO work tree still contains mounted image files."
  echo "Run: sudo scripts/clean-iso.sh${1:+ $1}"
  exit 1
fi

daisyos_unmount_iso_work_tree "$work_dir"

if [[ "${EUID:-$(id -u)}" -ne 0 && -d "$work_dir" ]] \
    && find "$work_dir" ! -user "$(id -u)" -print -quit | grep -q .; then
  echo "The ISO work tree contains protected image files."
  echo "Run: sudo scripts/clean-iso.sh${1:+ $1}"
  exit 1
fi

rm -rf "$work_dir"
echo "Removed archiso work directory."

if [[ "${1:-}" == "--out" ]]; then
  rm -rf "$repo_root/out"
  echo "Removed ISO output directory."
fi
