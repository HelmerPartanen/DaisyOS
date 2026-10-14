#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
profile="$repo_root/os/archiso/DaisyOS"
staged_shell_dir="$profile/airootfs/opt/DaisyOS/shell"
artifact_dir="$repo_root/artifacts/live-iso-alpha"
iso_path=""

remove_extracted_tree() {
  local path="$1"
  if [[ -d "$path" ]]; then
    chmod -R u+w "$path" 2>/dev/null || true
  fi
  rm -rf "$path"
}

usage() {
  cat <<'EOF'
Usage: scripts/validate-live-iso-alpha.sh [ISO]

Runs the complete DaisyOS bootable live-ISO alpha acceptance gate. The image
must be newer than its staged profile and contain the current production shell.
EOF
}

while (($# > 0)); do
  case "$1" in
    --help|-h)
      usage
      exit 0
      ;;
    --*)
      echo "Unknown option: $1" >&2
      usage >&2
      exit 2
      ;;
    *)
      if [[ -n "$iso_path" ]]; then
        echo "Only one ISO path may be supplied." >&2
        exit 2
      fi
      iso_path="$1"
      shift
      ;;
  esac
done

if [[ -z "$iso_path" ]]; then
  iso_path="$(find "$repo_root/out" -maxdepth 1 -type f -name 'DaisyOS-*.iso' -printf '%T@ %p\n' 2>/dev/null \
    | sort -nr | sed -n '1s/^[^ ]* //p')"
fi
if [[ -z "$iso_path" || ! -f "$iso_path" ]]; then
  echo "No DaisyOS ISO was found. Build one with sudo scripts/build-iso.sh." >&2
  exit 1
fi
iso_path="$(realpath "$iso_path")"

required_commands=(git magick qemu-system-x86_64 sha256sum socat unsquashfs xorriso)
for command_name in "${required_commands[@]}"; do
  if ! command -v "$command_name" >/dev/null 2>&1; then
    echo "$command_name is required for live-ISO alpha validation." >&2
    exit 1
  fi
done

provenance="$staged_shell_dir/.daisyos-build-provenance"
if [[ ! -x "$staged_shell_dir/DaisyOS.Shell" || ! -f "$provenance" ]]; then
  echo "The production shell and its provenance are not staged in the ISO profile." >&2
  echo "Run scripts/copy-shell-to-iso.sh first." >&2
  exit 1
fi

source_fingerprint=""
source_revision=""
shell_sha256=""
payload_sha256=""
publish_mode=""
# shellcheck disable=SC1090
source "$provenance"
current_fingerprint="$("$repo_root/scripts/source-fingerprint.sh")"
staged_shell_sha256="$(sha256sum "$staged_shell_dir/DaisyOS.Shell" | awk '{print $1}')"
staged_payload_sha256="$("$repo_root/scripts/payload-fingerprint.sh" "$staged_shell_dir")"
if [[ "$publish_mode" != "production" \
  || "$source_fingerprint" != "$current_fingerprint" \
  || "$shell_sha256" != "$staged_shell_sha256" \
  || -z "$payload_sha256" \
  || "$payload_sha256" != "$staged_payload_sha256" ]]; then
  echo "The staged shell does not match the current production source checkout." >&2
  echo "Publish and stage the shell again before building the ISO." >&2
  exit 1
fi

newer_profile_file="$(find "$profile" -type f -newer "$iso_path" -print -quit)"
if [[ -n "$newer_profile_file" ]]; then
  echo "The ISO is stale; this profile file is newer:" >&2
  echo "  ${newer_profile_file#"$repo_root/"}" >&2
  echo "Rebuild with sudo scripts/build-iso.sh." >&2
  exit 1
fi

remove_extracted_tree "$artifact_dir"
mkdir -p "$artifact_dir/extracted"
log_path="$artifact_dir/validation.log"
exec > >(tee "$log_path") 2>&1

echo "DaisyOS bootable live-ISO alpha validation"
echo "ISO: ${iso_path#"$repo_root/"}"
echo "Source revision: $source_revision"
echo "Source fingerprint: $source_fingerprint"

echo "[1/6] Verifying repository and ISO profile"
"$repo_root/scripts/test.sh"
"$repo_root/scripts/format-check.sh"

echo "[2/6] Verifying the production payload inside the ISO"
squashfs="$artifact_dir/extracted/airootfs.sfs"
xorriso -osirrox on -indev "$iso_path" \
  -extract /arch/x86_64/airootfs.sfs "$squashfs" >/dev/null 2>&1
embedded_shell_sha256="$(unsquashfs -cat "$squashfs" opt/DaisyOS/shell/DaisyOS.Shell 2>/dev/null | sha256sum | awk '{print $1}')"
embedded_provenance="$(unsquashfs -cat "$squashfs" opt/DaisyOS/shell/.daisyos-build-provenance 2>/dev/null)"
unsquashfs -quiet -d "$artifact_dir/extracted/root" "$squashfs" opt/DaisyOS/shell >/dev/null
embedded_payload_sha256="$("$repo_root/scripts/payload-fingerprint.sh" "$artifact_dir/extracted/root/opt/DaisyOS/shell")"
if [[ "$embedded_shell_sha256" != "$shell_sha256" ]] \
  || [[ "$embedded_payload_sha256" != "$payload_sha256" ]] \
  || ! grep -Fxq "source_fingerprint=$source_fingerprint" <<<"$embedded_provenance" \
  || ! grep -Fxq "payload_sha256=$payload_sha256" <<<"$embedded_provenance" \
  || ! grep -Fxq 'publish_mode=production' <<<"$embedded_provenance"; then
  echo "The ISO does not contain the validated production shell payload." >&2
  exit 1
fi
remove_extracted_tree "$artifact_dir/extracted"

echo "[3/6] Booting and rendering through BIOS"
"$repo_root/scripts/smoke-test-iso.sh" --firmware bios --exercise-recovery "$iso_path"

echo "[4/6] Booting and rendering through UEFI"
"$repo_root/scripts/smoke-test-iso.sh" --firmware uefi "$iso_path"

echo "[5/6] Exercising Restart and Shut down from the real shell"
"$repo_root/scripts/test-iso-power.sh" --action all "$iso_path"

echo "[6/6] Recording immutable acceptance evidence"
iso_sha256="$(sha256sum "$iso_path" | awk '{print $1}')"
iso_size="$(stat -c '%s' "$iso_path")"
generated_at="$(date --iso-8601=seconds)"
cat > "$artifact_dir/report.md" <<EOF
# DaisyOS bootable live-ISO alpha acceptance

Status: **PASS**

- Validated: $generated_at
- ISO: \`${iso_path#"$repo_root/"}\`
- ISO bytes: $iso_size
- ISO SHA-256: \`$iso_sha256\`
- Source revision: \`$source_revision\`
- Source fingerprint: \`$source_fingerprint\`
- Embedded shell SHA-256: \`$embedded_shell_sha256\`
- Embedded payload SHA-256: \`$embedded_payload_sha256\`
- Repository build/tests/profile: PASS
- BIOS graphical boot and stability: PASS
- Supervised shell crash recovery: PASS
- UEFI graphical boot and stability: PASS
- Shell Restart action: PASS
- Shell Shut down action: PASS

This certifies the non-installing live environment as an alpha. It does not
certify the mock-only installer, package publication, secure updates, hardware
compatibility beyond the tested virtual machines, or production security.
EOF

printf '%s  %s\n' "$iso_sha256" "$(basename "$iso_path")" > "$artifact_dir/$(basename "$iso_path").sha256"
echo "Bootable live-ISO alpha acceptance passed."
echo "Evidence: ${artifact_dir#"$repo_root/"}/report.md"
