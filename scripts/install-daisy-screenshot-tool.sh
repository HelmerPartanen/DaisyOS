#!/usr/bin/env bash
set -Eeuo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
source_dir="$repo_root/src/native/daisy_screenshot"
prefix="/"
build_type="Release"

usage() {
  cat <<'EOF'
Usage: scripts/install-daisy-screenshot-tool.sh [--prefix PATH] [--debug]

Builds and installs DaisyOS's trusted KWin screenshot helper. The helper is
root-owned but never setuid: it runs with the invoking user's permissions and
is trusted by KWin only for the Screenshot2 D-Bus interface.
EOF
}

while (($# > 0)); do
  case "$1" in
    --prefix)
      [[ $# -ge 2 ]] || { echo "--prefix requires a path." >&2; exit 2; }
      prefix="$2"
      shift 2
      ;;
    --debug)
      build_type="Debug"
      shift
      ;;
    --help|-h)
      usage
      exit 0
      ;;
    *)
      echo "Unknown option: $1" >&2
      usage >&2
      exit 2
      ;;
  esac
done

if [[ "$prefix" == "/" && "$EUID" -ne 0 ]]; then
  echo "Administrator privileges are required. Run: sudo $0" >&2
  exit 1
fi

for command in cmake ninja c++; do
  command -v "$command" >/dev/null 2>&1 || {
    echo "Missing build dependency: $command" >&2
    exit 1
  }
done

[[ -f "$source_dir/CMakeLists.txt" && -f "$source_dir/daisy_screenshot_helper.cpp" ]] || {
  echo "DaisyOS screenshot helper source is missing." >&2
  exit 1
}

build_dir="$(mktemp -d "${TMPDIR:-/tmp}/daisyos-screenshot.XXXXXX")"
trap 'rm -rf -- "$build_dir"' EXIT
if [[ "$prefix" == "/" ]]; then
  install_prefix="/usr/local"
  applications_dir="/usr/share/applications"
else
  install_prefix="$prefix"
  applications_dir="$prefix/share/applications"
fi

cmake -S "$source_dir" -B "$build_dir" -G Ninja -DCMAKE_BUILD_TYPE="$build_type"
cmake --build "$build_dir"
cmake --install "$build_dir" --prefix "$install_prefix"

libexec_dir="$install_prefix/libexec/daisyos"
install -d -m 0755 "$libexec_dir"
chmod 0755 "$libexec_dir"
if [[ -f "$libexec_dir/daisy-screenshot-helper" ]]; then
  chmod 0755 "$libexec_dir/daisy-screenshot-helper"
  install -m 0644 /dev/null "$libexec_dir/daisy-screenshot-helper.api-v2"
fi

install -d -m 0755 "$applications_dir"

# The desktop file Exec= must exactly match the installed binary path so KWin
# can authorize the process via its ApplicationsRestricted DBus interface check.
helper_binary="$libexec_dir/daisy-screenshot-helper"
desktop_src="$source_dir/org.daisyos.screenshot-helper.desktop"
desktop_dst="$applications_dir/org.daisyos.screenshot-helper.desktop"

# Rewrite Exec= to point at the actual installed binary path
sed "s|^Exec=.*|Exec=$helper_binary|" "$desktop_src" > "$desktop_dst"
chmod 0644 "$desktop_dst"

command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "$applications_dir" || true
command -v kbuildsycoca6 >/dev/null 2>&1 && XDG_MENU_PREFIX="${XDG_MENU_PREFIX:-plasma-}" kbuildsycoca6 --noincremental || true

echo "Installed DaisyOS Screenshot helper at $helper_binary"
echo "Desktop file installed at $desktop_dst with Exec=$helper_binary"
echo "Sign out and back into DaisyOS Native Development before the first capture."
