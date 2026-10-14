#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

if ! command -v dotnet >/dev/null 2>&1; then
  echo "Error: dotnet is missing. Install .NET SDK 10 before building." >&2
  exit 1
fi

source "$repo_root/scripts/release-common.sh"
version="${DAISYOS_VERSION:-$(daisyos_version_from_props "$repo_root/Directory.Build.props")}"
channel="${DAISYOS_RELEASE_CHANNEL:-nightly}"
build_date="${DAISYOS_BUILD_DATE:-$(date -u +%Y%m%d)}"
revision="$(daisyos_short_revision)"
daisyos_validate_version "$version"
daisyos_validate_channel "$channel"

properties=(
  "-p:VersionPrefix=$version"
  "-p:DaisyOSVersionChannel=$channel"
  "-p:DaisyOSBuildDate=$build_date"
  "-p:DaisyOSSourceRevision=$revision"
)

dotnet restore "${properties[@]}"
dotnet build --configuration Debug --no-restore "${properties[@]}"
dotnet test --configuration Debug --no-build "${properties[@]}"

echo "Debug build and tests passed: $version-$channel ($revision)"

