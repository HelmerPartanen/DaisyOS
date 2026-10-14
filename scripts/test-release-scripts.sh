#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "$repo_root/scripts/release-common.sh"

fail() { echo "FAIL: $*" >&2; exit 1; }
assert_equal() { [[ "$1" == "$2" ]] || fail "expected '$2', got '$1'"; }

daisyos_validate_channel nightly
daisyos_validate_channel beta
daisyos_validate_channel stable
if daisyos_validate_channel edge >/dev/null 2>&1; then fail "invalid channel was accepted"; fi
daisyos_validate_version 0.1.0
daisyos_validate_version 1.2.3-alpha.1
if daisyos_validate_version latest >/dev/null 2>&1; then fail "invalid version was accepted"; fi

assert_equal "$(daisyos_artifact_basename 0.1.0 nightly)" "DaisyOS-Shell-0.1.0-nightly-linux-x64"
assert_equal "$(daisyos_version_from_props "$repo_root/Directory.Build.props")" "0.1.0"

config="$(DAISYOS_VERSION=9.9.9 DAISYOS_RELEASE_CHANNEL=beta DAISYOS_RELEASE_OUTPUT=/tmp/from-env \
  "$repo_root/scripts/build-release.sh" --version 1.2.3 --channel stable --output /tmp/from-cli --print-config)"
[[ "$config" == *$'version=1.2.3\nchannel=stable\noutput=/tmp/from-cli'* ]] \
  || fail "CLI arguments did not override environment values"

publish_help="$("$repo_root/scripts/publish-shell.sh" --help)"
[[ "$publish_help" == *"--fast"* ]] || fail "fast publish mode is missing from help"
if "$repo_root/scripts/publish-shell.sh" --unknown >/dev/null 2>&1; then
  fail "publish-shell.sh accepted an unknown option"
fi

temp_dir="$(mktemp -d)"
trap 'rm -rf "$temp_dir"' EXIT
printf 'DaisyOS release test\n' > "$temp_dir/artifact.tar.gz"
daisyos_write_checksum "$temp_dir/artifact.tar.gz" "$temp_dir/artifact.tar.gz.sha256"
(cd "$temp_dir" && sha256sum --check artifact.tar.gz.sha256 >/dev/null) \
  || fail "generated checksum could not be verified"

echo "Release script tests passed."
