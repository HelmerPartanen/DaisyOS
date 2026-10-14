#!/usr/bin/env bash

# Shared safety helpers for disposable mkarchiso work trees. This file is
# sourced by build/cleanup scripts and intentionally performs no work itself.

daisyos_assert_safe_iso_work_dir() {
  local repo_root="$1"
  local work_dir="$2"
  local expected
  local actual

  expected="$(realpath -m "$repo_root/work/archiso")"
  actual="$(realpath -m "$work_dir")"
  if [[ "$actual" != "$expected" || "$actual" == "/" || "$actual" == "$repo_root" ]]; then
    echo "Refusing to clean an unexpected ISO work directory: $actual" >&2
    return 1
  fi
}

daisyos_iso_work_tree_has_mounts() {
  local work_dir="$1"
  findmnt -Rrn -o TARGET "$work_dir" >/dev/null 2>&1
}

daisyos_unmount_iso_work_tree() {
  local work_dir="$1"
  local -a mount_targets=()
  local index

  mapfile -t mount_targets < <(findmnt -Rrn --raw -o TARGET "$work_dir" 2>/dev/null || true)
  for ((index = ${#mount_targets[@]} - 1; index >= 0; index--)); do
    echo "Unmounting stale ISO work-tree mount: ${mount_targets[index]}"
    if ! umount -- "${mount_targets[index]}"; then
      echo "Could not safely unmount ${mount_targets[index]}." >&2
      echo "The ISO work tree was left untouched. Close processes using it and try again." >&2
      return 1
    fi
  done

  if daisyos_iso_work_tree_has_mounts "$work_dir"; then
    echo "Refusing to clean the ISO work tree because mounted paths remain below it." >&2
    findmnt -R "$work_dir" >&2 || true
    return 1
  fi
}
