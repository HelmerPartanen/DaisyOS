#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
[[ "${1:-}" == --root && -n "${2:-}" && $# == 2 ]] || { echo 'Usage: install-application-management.sh --root PATH' >&2; exit 2; }
staging_root="$2"
build_directory="$(mktemp -d "${TMPDIR:-/tmp}/daisy-package-helper.XXXXXX")"
trap 'cmake -E remove_directory "$build_directory"' EXIT
cmake -S "$repo_root/src/native/daisy_package_helper" -B "$build_directory" -DCMAKE_BUILD_TYPE=Release
cmake --build "$build_directory"
install -Dm755 "$build_directory/daisy-package-helper" "$staging_root/usr/lib/daisyos/daisy-package-helper.new"
mv -f "$staging_root/usr/lib/daisyos/daisy-package-helper.new" "$staging_root/usr/lib/daisyos/daisy-package-helper"
install -Dm644 "$repo_root/packaging/applications/essential-applications.conf" "$staging_root/usr/share/daisyos/essential-applications.conf"
install -Dm644 "$repo_root/packaging/polkit/org.daisyos.applications.policy" "$staging_root/usr/share/polkit-1/actions/org.daisyos.applications.policy"
install -Dm644 "$repo_root/packaging/applications/org.daisyos.applications.xml" "$staging_root/usr/share/mime/packages/org.daisyos.applications.xml"
