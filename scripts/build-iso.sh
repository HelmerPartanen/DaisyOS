#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "$repo_root/scripts/iso-worktree-guard.sh"
profile="$repo_root/os/archiso/DaisyOS"
work_dir="$repo_root/work/archiso"
out_dir="$repo_root/out"

daisyos_assert_safe_iso_work_dir "$repo_root" "$work_dir"

if ! command -v mkarchiso >/dev/null 2>&1; then
  echo "mkarchiso is missing. Install Arch archiso before building an ISO."
  exit 1
fi

if [[ "${EUID:-$(id -u)}" -ne 0 ]]; then
  echo "Building an Arch ISO requires temporary administrator access for the isolated image filesystem."
  echo "Run: sudo scripts/build-iso.sh"
  exit 1
fi

"$repo_root/scripts/check-iso-profile.sh"

shell_binary="$profile/airootfs/opt/DaisyOS/shell/DaisyOS.Shell"
if [[ ! -x "$shell_binary" ]]; then
  echo "The published shell is not staged in the ISO profile."
  echo "Run scripts/publish-shell.sh and scripts/copy-shell-to-iso.sh first."
  exit 1
fi

# A live-alpha candidate must never inherit files from a previous image root.
# mkarchiso's work tree is disposable and kept entirely inside the repository.
daisyos_unmount_iso_work_tree "$work_dir"
rm -rf "$work_dir"
mkdir -p "$work_dir" "$out_dir"
rm -f "$out_dir/DaisyOS-$(date +%Y.%m.%d)-x86_64.iso"

cleanup_build_mounts() {
  local status=$?
  trap - EXIT
  if ! daisyos_unmount_iso_work_tree "$work_dir"; then
    status=1
  fi
  exit "$status"
}
trap cleanup_build_mounts EXIT

mkarchiso -v -w "$work_dir" -o "$out_dir" "$profile"
daisyos_unmount_iso_work_tree "$work_dir"

if [[ -n "${SUDO_UID:-}" && -n "${SUDO_GID:-}" ]]; then
  chown -R "$SUDO_UID:$SUDO_GID" "$work_dir" "$out_dir"
fi

trap - EXIT
