#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"
source "$repo_root/scripts/release-common.sh"

if ! command -v dotnet >/dev/null 2>&1; then
  echo "dotnet is missing. Install .NET SDK 10 before publishing."
  exit 1
fi

"$repo_root/scripts/build-mpv-wallpaper.sh"
local_mpv_prefix="${DAISYOS_MPV_PREFIX:-${XDG_DATA_HOME:-$HOME/.local/share}/DaisyOS/mpv-gpu}"
if ! pkg-config --exists mpv && [[ -d "$local_mpv_prefix/lib/pkgconfig" ]]; then
  export PKG_CONFIG_PATH="$local_mpv_prefix/lib/pkgconfig${PKG_CONFIG_PATH:+:$PKG_CONFIG_PATH}"
fi

output_dir="publish/linux-x64"
output_mode_file="${output_dir}.publish-mode"
version="${DAISYOS_VERSION:-$(daisyos_version_from_props "$repo_root/Directory.Build.props")}"
channel="${DAISYOS_RELEASE_CHANNEL:-nightly}"
build_date="${DAISYOS_BUILD_DATE:-$(date -u +%Y%m%d)}"
revision="$(daisyos_short_revision)"
fast=false

while [[ $# -gt 0 ]]; do
  case "$1" in
    --fast)
      fast=true
      shift
      ;;
    -h|--help)
      echo "Usage: scripts/publish-shell.sh [--fast]"
      echo ""
      echo "  --fast  Skip production ReadyToRun precompilation for local testing."
      exit 0
      ;;
    *)
      echo "Unknown option: $1" >&2
      echo "Usage: scripts/publish-shell.sh [--fast]" >&2
      exit 1
      ;;
  esac
done

"$repo_root/scripts/build-daisy-launch.sh"
"$repo_root/scripts/build-busy-cursor.sh"
python3 "$repo_root/scripts/build-cursor-themes.py"

daisyos_validate_version "$version"
daisyos_validate_channel "$channel"

publish_properties=(
  -p:VersionPrefix="$version"
  -p:DaisyOSVersionChannel="$channel"
  -p:DaisyOSBuildDate="$build_date"
  -p:DaisyOSSourceRevision="$revision"
)

if [[ "$fast" == true ]]; then
  publish_properties+=(
    -p:PublishReadyToRun=false
    -p:PublishReadyToRunComposite=false
  )
  echo "Fast local publish: skipping production ReadyToRun precompilation."
else
  publish_properties+=(
    -p:PublishReadyToRun=true
    -p:PublishReadyToRunComposite=true
  )
fi

rm -rf "$output_dir"
rm -f "$output_mode_file"
for application in Files Notes Settings DiskManager; do
  dotnet restore "src/DaisyOS.$application/DaisyOS.$application.csproj" \
    --runtime linux-x64 "${publish_properties[@]}"
done
dotnet publish src/DaisyOS.Shell/DaisyOS.Shell.csproj \
  --configuration Release \
  --runtime linux-x64 \
  --self-contained true \
  --no-restore \
  --output "$output_dir" \
  "${publish_properties[@]}"

install -m755 "$repo_root/services/DaisyLaunch/build/daisy-launch" "$output_dir/daisy-launch"
install -m755 "$repo_root/src/native/daisy_busy_cursor/build/daisyos-busy-cursor-v1.so" "$output_dir/daisyos-busy-cursor-v1.so"

mkdir -p "$output_dir/cursor-themes"
cp -a "$repo_root/artifacts/cursors/"DaisyOS-* "$output_dir/cursor-themes/"

# Publish into its own directory so composite ReadyToRun ownership cannot
# overwrite the desktop’s runtime assemblies.
for application in Files Notes Settings DiskManager; do
  dotnet publish "src/DaisyOS.$application/DaisyOS.$application.csproj" \
    --configuration Release --runtime linux-x64 --self-contained true \
    --no-restore --output "$output_dir/apps/$application" "${publish_properties[@]}"
done

if [[ "$fast" == true ]]; then
  printf 'fast\n' > "$output_mode_file"
else
  printf 'production\n' > "$output_mode_file"
fi

echo ""
echo "Publish complete: $output_dir"
if [[ "$fast" == true ]]; then
  echo "Mode: fast local build (not suitable for ISO or release images)"
else
  echo "Mode: production ReadyToRun build"
fi
echo ""

{
  printf 'wallpaper-backend=libmpv-opengl-subsurface\n'
  for dependency in mpv egl wayland-client libavformat libavutil; do
    printf '%s=%s\n' "$dependency" "$(pkg-config --modversion "$dependency")"
  done
  mpv_binary="$(pkg-config --variable=prefix mpv)/bin/mpv"
  if [[ -x "$mpv_binary" ]]; then
    "$mpv_binary" --version
  else
    echo 'mpv-player-version=unavailable (mpv above is the client API version)'
  fi
  ffmpeg -version | sed -n '1p'
} > "$output_dir/wallpaper-dependencies.txt"

shell_binary="$output_dir/DaisyOS.Shell"
if [ -f "$shell_binary" ]; then
  chmod +x "$shell_binary"
  echo "Binary: $shell_binary"
  echo "Size: $(du -sh "$output_dir" | cut -f1)"
  echo ""
  echo "To test the published binary (headless, outside dotnet run):"
  echo "  DAISYOS_SHELL_SERVICES=mock $shell_binary"
  echo ""
  echo "To copy to ISO profile:"
  echo "  scripts/copy-shell-to-iso.sh"
else
  echo "Warning: Binary not found at expected path."
  echo "Check $output_dir for the published files."
fi
