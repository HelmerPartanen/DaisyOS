#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

required_files=(
  docs/branding.md
  src/DaisyOS.Core/Models/BrandInfo.cs
  src/DaisyOS.Shell/Assets/Brand/favicon.svg
  src/DaisyOS.Shell/Assets/Sounds/StartupSound.mp3
  src/DaisyOS.Shell/Assets/Sounds/manifest.json
  "src/DaisyOS.Shell/Assets/fonts/Inter-VariableFont_opsz,wght.ttf"
  os/branding/boot/daisyos-logo.png
  os/branding/boot/daisyos.plymouth
  os/branding/boot/daisyos.script
  os/archiso/DaisyOS/airootfs/etc/issue
  os/archiso/DaisyOS/airootfs/etc/motd
  os/archiso/DaisyOS/airootfs/usr/local/bin/DaisyOS-start-shell
  os/archiso/DaisyOS/airootfs/usr/local/bin/daisyos-setup.sh
  os/archiso/DaisyOS/airootfs/usr/local/bin/daisyos-session
  os/archiso/DaisyOS/airootfs/usr/local/bin/daisyos-fallback-session
  os/archiso/DaisyOS/airootfs/usr/local/bin/daisyos-recover-session
)

for file in "${required_files[@]}"; do
  if [[ ! -s "$repo_root/$file" ]]; then
    echo "Missing or empty brand asset: $file"
    exit 1
  fi
done

if command -v rg >/dev/null 2>&1; then
  if rg -n --hidden \
      -g '!**/.git/**' -g '!**/bin/**' -g '!**/obj/**' \
      -g '!docs/branding.md' \
      -g '!scripts/check-branding.sh' \
      'DottOS|dottos|DOTTOS|DottOs' "$repo_root"; then
    echo "Legacy product branding remains in the repository."
    exit 1
  fi
else
  echo "rg (ripgrep) is unavailable; legacy-branding text search was skipped."
fi

grep -Fq 'iso_name="DaisyOS"' "$repo_root/os/archiso/DaisyOS/profiledef.sh"
grep -Fq 'iso_label="DAISYOS_' "$repo_root/os/archiso/DaisyOS/profiledef.sh"
grep -Fq "pkgname=daisyos-shell" "$repo_root/packaging/arch/PKGBUILD"
grep -Fq 'public const string ProductName = "DaisyOS"' \
  "$repo_root/src/DaisyOS.Core/Models/BrandInfo.cs"
grep -Fq 'public const string DefaultHostName = "daisyos"' \
  "$repo_root/src/DaisyOS.Core/Models/BrandInfo.cs"

if command -v jq >/dev/null 2>&1; then
  jq -e '.schemaVersion == 1 and .soundTheme == "DaisyOS" and .events.startup == "StartupSound.mp3"' \
    "$repo_root/src/DaisyOS.Shell/Assets/Sounds/manifest.json" >/dev/null
else
  echo "jq is unavailable; sound-manifest schema validation was skipped."
fi

if command -v shellcheck >/dev/null 2>&1; then
  shellcheck \
    "$repo_root/os/archiso/DaisyOS/airootfs/usr/local/bin/DaisyOS-start-shell" \
    "$repo_root/os/archiso/DaisyOS/airootfs/usr/local/bin/daisyos-setup.sh" \
    "$repo_root/os/archiso/DaisyOS/airootfs/usr/local/bin/daisyos-session" \
    "$repo_root/os/archiso/DaisyOS/airootfs/usr/local/bin/daisyos-fallback-session" \
    "$repo_root/os/archiso/DaisyOS/airootfs/usr/local/bin/daisyos-recover-session"
else
  echo "ShellCheck is unavailable; live-image helper lint was skipped."
fi

echo "DaisyOS branding checks passed."
