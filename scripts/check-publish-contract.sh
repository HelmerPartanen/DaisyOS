#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

for script in scripts/publish-shell.sh scripts/build-release.sh; do
  path="$repo_root/$script"
  if ! grep -Fq -- 'PublishReadyToRun=true' "$path"; then
    echo "$script must precompile production shell code for reliable VM startup."
    exit 1
  fi
  if ! grep -Fq -- 'PublishReadyToRunComposite=true' "$path"; then
    echo "$script must emit the composite ReadyToRun image used by the live ISO."
    exit 1
  fi
  if [[ "$(grep -Fc -- '--runtime linux-x64' "$path")" -lt 2 ]]; then
    echo "$script must restore and publish for linux-x64 explicitly."
    exit 1
  fi
  if ! grep -Fq -- '--no-restore' "$path"; then
    echo "$script must publish from the explicitly restored linux-x64 assets."
    exit 1
  fi
done

if ! grep -Fq 'output_dir="publish/linux-x64"' "$repo_root/scripts/publish-shell.sh"; then
  echo "The ISO-compatible publish directory changed unexpectedly."
  exit 1
fi

if ! grep -Fq -- '--fast)' "$repo_root/scripts/publish-shell.sh" ||
   ! grep -Fq -- '-p:PublishReadyToRun=false' "$repo_root/scripts/publish-shell.sh"; then
  echo "publish-shell.sh must retain the fast local development mode."
  exit 1
fi

if ! grep -Fq 'publish-mode' "$repo_root/scripts/copy-shell-to-iso.sh"; then
  echo "ISO staging must reject fast local publish output."
  exit 1
fi

if ! grep -Fq '.daisyos-build-provenance' "$repo_root/scripts/copy-shell-to-iso.sh" \
  || ! grep -Fq 'source-fingerprint.sh' "$repo_root/scripts/copy-shell-to-iso.sh" \
  || ! grep -Fq 'payload-fingerprint.sh' "$repo_root/scripts/copy-shell-to-iso.sh"; then
  echo "ISO staging must bind the production payload to the current source tree."
  exit 1
fi

alpha_gate="$repo_root/scripts/validate-live-iso-alpha.sh"
for required_contract in \
  'source_fingerprint' \
  'payload_sha256' \
  'embedded_payload_sha256' \
  'embedded_shell_sha256' \
  '--firmware bios --exercise-recovery' \
  '--firmware uefi' \
  'test-iso-power.sh'; do
  if ! grep -Fq -- "$required_contract" "$alpha_gate"; then
    echo "The live-ISO alpha gate is missing: $required_contract"
    exit 1
  fi
done

echo "Production publish contract checks passed."
