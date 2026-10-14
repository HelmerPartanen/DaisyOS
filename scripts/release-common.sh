#!/usr/bin/env bash

# Shared release helpers. This file is sourced by build and test scripts.

daisyos_release_error() {
  echo "Error: $*" >&2
  return 1
}

daisyos_validate_channel() {
  case "$1" in
    nightly|beta|stable) return 0 ;;
    *) daisyos_release_error "unsupported release channel '$1' (expected nightly, beta, or stable)." ;;
  esac
}

daisyos_validate_version() {
  if [[ ! "$1" =~ ^[0-9]+\.[0-9]+\.[0-9]+([.-][0-9A-Za-z][0-9A-Za-z.-]*)?$ ]]; then
    daisyos_release_error "invalid version '$1' (expected a SemVer value such as 0.1.0)."
  fi
}

daisyos_version_from_props() {
  local props_file="$1"
  local version
  version="$(sed -nE 's|.*<VersionPrefix[^>]*>([^<]+)</VersionPrefix>.*|\1|p' "$props_file" | head -n 1)"
  if [[ -z "$version" ]]; then
    daisyos_release_error "could not read VersionPrefix from $props_file."
    return 1
  fi
  printf '%s\n' "$version"
}

daisyos_short_revision() {
  if [[ -n "${DAISYOS_SOURCE_REVISION:-}" ]]; then
    printf '%s\n' "${DAISYOS_SOURCE_REVISION:0:12}"
  elif command -v git >/dev/null 2>&1 && git rev-parse --is-inside-work-tree >/dev/null 2>&1; then
    git rev-parse --short=12 HEAD
  else
    printf '%s\n' "local"
  fi
}

daisyos_artifact_basename() {
  printf 'DaisyOS-Shell-%s-%s-linux-x64\n' "$1" "$2"
}

daisyos_write_checksum() {
  local archive="$1"
  local checksum_file="$2"
  local archive_dir archive_name
  archive_dir="$(cd "$(dirname "$archive")" && pwd)"
  archive_name="$(basename "$archive")"
  (cd "$archive_dir" && sha256sum "$archive_name") > "$checksum_file"
}

