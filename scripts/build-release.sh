#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"
source "$repo_root/scripts/release-common.sh"

channel="${DAISYOS_RELEASE_CHANNEL:-nightly}"
version="${DAISYOS_VERSION:-$(daisyos_version_from_props "$repo_root/Directory.Build.props")}"
output_dir="${DAISYOS_RELEASE_OUTPUT:-$repo_root/artifacts/release}"
print_config=false

while [[ $# -gt 0 ]]; do
  case "$1" in
    --channel)
      [[ $# -ge 2 ]] || { daisyos_release_error "--channel requires a value."; exit 1; }
      channel="$2"; shift 2 ;;
    --version)
      [[ $# -ge 2 ]] || { daisyos_release_error "--version requires a value."; exit 1; }
      version="$2"; shift 2 ;;
    --output)
      [[ $# -ge 2 ]] || { daisyos_release_error "--output requires a value."; exit 1; }
      output_dir="$2"; shift 2 ;;
    --print-config)
      print_config=true; shift ;;
    -h|--help)
      echo "Usage: scripts/build-release.sh [--channel nightly|beta|stable] [--version SEMVER] [--output DIRECTORY]"
      exit 0 ;;
    *)
      daisyos_release_error "unknown argument '$1'."; exit 1 ;;
  esac
done

daisyos_validate_channel "$channel"
daisyos_validate_version "$version"

artifact_name="$(daisyos_artifact_basename "$version" "$channel")"
if [[ "$print_config" == true ]]; then
  printf 'version=%s\nchannel=%s\noutput=%s\nartifact=%s\n' "$version" "$channel" "$output_dir" "$artifact_name"
  exit 0
fi

for command_name in dotnet tar sha256sum; do
  if ! command -v "$command_name" >/dev/null 2>&1; then
    daisyos_release_error "$command_name is required for release builds."
    exit 1
  fi
done

build_date="${DAISYOS_BUILD_DATE:-$(date -u +%Y%m%d)}"
revision="$(daisyos_short_revision)"
publish_dir="$repo_root/publish/linux-x64"
properties=(
  "-p:PublishReadyToRun=true"
  "-p:PublishReadyToRunComposite=true"
  "-p:VersionPrefix=$version"
  "-p:DaisyOSVersionChannel=$channel"
  "-p:DaisyOSBuildDate=$build_date"
  "-p:DaisyOSSourceRevision=$revision"
)

"$repo_root/scripts/build-desktop-services.sh"
"$repo_root/scripts/format-check.sh"
dotnet restore
dotnet build --configuration Release --no-restore "${properties[@]}"
dotnet test --configuration Release --no-build "${properties[@]}"
rm -rf "$publish_dir"
for application in Files Notes Settings DiskManager AppInstaller; do
  dotnet restore "src/DaisyOS.$application/DaisyOS.$application.csproj" \
    --runtime linux-x64 "${properties[@]}"
done
dotnet publish src/DaisyOS.Shell/DaisyOS.Shell.csproj \
  --configuration Release \
  --runtime linux-x64 \
  --self-contained true \
  --no-restore \
  --output "$publish_dir" \
  "${properties[@]}"

for application in Files Notes Settings DiskManager AppInstaller; do
  dotnet publish "src/DaisyOS.$application/DaisyOS.$application.csproj" \
    --configuration Release --runtime linux-x64 --self-contained true \
    --no-restore --output "$publish_dir/apps/$application" "${properties[@]}"
done

shell_binary="$publish_dir/DaisyOS.Shell"
if [[ ! -f "$shell_binary" ]]; then
  daisyos_release_error "published shell binary was not created at $shell_binary."
  exit 1
fi
chmod +x "$shell_binary"

mkdir -p "$output_dir"
find "$output_dir" -maxdepth 1 -type f \
  \( -name 'DaisyOS-Shell-*-linux-x64.tar.gz' -o -name 'DaisyOS-Shell-*-linux-x64.tar.gz.sha256' \) \
  -delete
archive="$output_dir/$artifact_name.tar.gz"
checksum="$archive.sha256"
tar -C "$publish_dir" -czf "$archive" .
daisyos_write_checksum "$archive" "$checksum"
(cd "$output_dir" && sha256sum --check "$(basename "$checksum")")

echo "Release build complete"
echo "  Version:  $version-$channel"
echo "  Revision: $revision"
echo "  Publish:  $publish_dir"
echo "  Archive:  $archive"
echo "  SHA-256:  $checksum"
