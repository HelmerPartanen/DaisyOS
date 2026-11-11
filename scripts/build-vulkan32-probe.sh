#!/usr/bin/env bash
set -Eeuo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
output="$repo_root/out/daisyos-vulkan32-probe"
install_probe=false

usage() {
  cat <<'HELP'
Usage: scripts/build-vulkan32-probe.sh [--output PATH] [--install]

Build the optional 32-bit Vulkan physical-device enumeration probe. This needs
a working GCC -m32 toolchain, Vulkan headers, and a linkable 32-bit Vulkan loader.
By default it writes to out/; --install places it at
/usr/lib/daisyos/daisyos-vulkan32-probe for the diagnostics command to discover.
HELP
}

while (($#)); do
  case "$1" in
    --output)
      [[ $# -ge 2 && -n "$2" ]] || { echo '--output requires a path.' >&2; exit 2; }
      output="$2"; shift 2 ;;
    --install) install_probe=true; shift ;;
    --help|-h) usage; exit 0 ;;
    *) usage >&2; exit 2 ;;
  esac
done

command -v gcc >/dev/null || { echo 'gcc is required.' >&2; exit 1; }
command -v file >/dev/null || { echo 'file is required to verify the output architecture.' >&2; exit 1; }
mkdir -p "$(dirname "$output")"
if ! gcc -m32 -O2 -Wall -Wextra -Werror \
    "$repo_root/src/native/daisy_vulkan_probe/main.c" -lvulkan -o "$output"; then
  echo 'Could not build the 32-bit Vulkan probe. Install a working 32-bit C runtime, Vulkan headers, and 32-bit Vulkan loader development files.' >&2
  exit 1
fi

if ! file "$output" | grep -Eq 'ELF 32-bit.*Intel 80386'; then
  rm -f -- "$output"
  echo 'The compiler did not produce an i686 32-bit executable.' >&2
  exit 1
fi

if [[ "$install_probe" == true ]]; then
  install -Dm0755 "$output" /usr/lib/daisyos/daisyos-vulkan32-probe
  echo 'Installed /usr/lib/daisyos/daisyos-vulkan32-probe'
else
  echo "Built $output"
fi
