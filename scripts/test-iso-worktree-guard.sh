#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "$repo_root/scripts/iso-worktree-guard.sh"

fail() {
  echo "ISO work-tree guard test failed: $*" >&2
  exit 1
}

work_dir="$repo_root/work/archiso"
daisyos_assert_safe_iso_work_dir "$repo_root" "$work_dir"
if daisyos_assert_safe_iso_work_dir "$repo_root" / >/dev/null 2>&1; then
  fail "the filesystem root was accepted as disposable work"
fi
if daisyos_assert_safe_iso_work_dir "$repo_root" "$repo_root" >/dev/null 2>&1; then
  fail "the repository root was accepted as disposable work"
fi

declare -a unmounted=()
findmnt() {
  if [[ " $* " == *" --raw "* ]]; then
    printf '%s\n' "$work_dir/proc" "$work_dir/proc/sys"
    return 0
  fi
  return 1
}
umount() {
  [[ "$1" == "--" ]] || fail "umount target was not option-safe"
  unmounted+=("$2")
}

daisyos_unmount_iso_work_tree "$work_dir"
[[ "${unmounted[*]}" == "$work_dir/proc/sys $work_dir/proc" ]] \
  || fail "nested mounts were not unmounted deepest-first"

umount() {
  return 1
}
if daisyos_unmount_iso_work_tree "$work_dir" >/dev/null 2>&1; then
  fail "cleanup continued after an unmount failure"
fi

echo "ISO work-tree guard tests passed."
