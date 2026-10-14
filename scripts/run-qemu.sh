#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
out_dir="$repo_root/out"
firmware="uefi"

usage() {
  cat <<'EOF'
Usage: scripts/run-qemu.sh [--firmware bios|uefi]

Starts the newest DaisyOS live ISO in an interactive QEMU window.
UEFI is the default; pass --firmware bios to exercise the fallback path.
EOF
}

while (( $# > 0 )); do
  case "$1" in
    --firmware)
      [[ $# -ge 2 ]] || { usage >&2; exit 2; }
      firmware="$2"
      shift 2
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

if [[ "$firmware" != "bios" && "$firmware" != "uefi" ]]; then
  echo "Firmware must be 'bios' or 'uefi'." >&2
  exit 2
fi

if ! command -v qemu-system-x86_64 >/dev/null 2>&1; then
  echo "qemu-system-x86_64 is missing. Install QEMU before running VM smoke tests."
  exit 1
fi

latest_iso="$(find "$out_dir" -maxdepth 1 -type f -name '*.iso' -printf '%T@ %p\n' 2>/dev/null | sort -nr | sed -n '1s/^[^ ]* //p')"

if [[ -z "${latest_iso:-}" ]]; then
  echo "No ISO found in $out_dir. Run scripts/build-iso.sh first."
  exit 1
fi

machine_args=(-machine q35 -accel "tcg,thread=multi" -cpu max)
acceleration="software emulation"
if [[ -r /dev/kvm && -w /dev/kvm ]]; then
  machine_args=(-machine q35 -accel kvm -cpu host)
  acceleration="KVM"
fi

firmware_args=()
video_args=(-vga std)
if [[ "$firmware" == "uefi" ]]; then
  ovmf_code="${DAISYOS_OVMF_CODE:-/usr/share/edk2/x64/OVMF_CODE.4m.fd}"
  ovmf_vars_template="${DAISYOS_OVMF_VARS:-/usr/share/edk2/x64/OVMF_VARS.4m.fd}"
  if [[ ! -f "$ovmf_code" || ! -f "$ovmf_vars_template" ]]; then
    echo "UEFI testing requires OVMF firmware files." >&2
    exit 1
  fi
  firmware_dir="$repo_root/artifacts/qemu"
  mkdir -p "$firmware_dir"
  ovmf_vars="$firmware_dir/OVMF_VARS.4m.fd"
  cp "$ovmf_vars_template" "$ovmf_vars"
  firmware_args=(
    -drive "if=pflash,format=raw,readonly=on,file=$ovmf_code"
    -drive "if=pflash,format=raw,file=$ovmf_vars"
  )
  video_args=(-vga std)
fi

printf 'Booting %s through %s with %s.\n' "$latest_iso" "${firmware^^}" "$acceleration"

qemu-system-x86_64 \
  "${machine_args[@]}" \
  "${firmware_args[@]}" \
  -m 4096 \
  -smp 4 \
  "${video_args[@]}" \
  -cdrom "$latest_iso" \
  -boot once=d \
  -display gtk,zoom-to-fit=on
