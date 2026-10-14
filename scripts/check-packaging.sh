#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
package_dir="$repo_root/packaging/arch"
release_mode=false
report_path=""

usage() {
  cat <<'EOF'
Usage: scripts/check-packaging.sh [--release] [--report PATH]

Validates package ownership, configuration preservation, service safety, and
repository policy without installing packages. --release additionally fails
while publication blockers such as placeholder checksums remain.
EOF
}

while (($# > 0)); do
  case "$1" in
    --release)
      release_mode=true
      shift
      ;;
    --report)
      if (($# < 2)); then
        echo "An output path is required after --report."
        exit 2
      fi
      report_path="$2"
      shift 2
      ;;
    --help|-h)
      usage
      exit 0
      ;;
    *)
      echo "Unknown option: $1"
      usage
      exit 2
      ;;
  esac
done

release_blockers=()

add_release_blocker() {
  release_blockers+=("$1")
}

require_text() {
  local file="$1"
  local pattern="$2"
  local message="$3"
  if ! grep -Eq "$pattern" "$file"; then
    echo "$message"
    exit 1
  fi
}

forbid_text() {
  local file="$1"
  local pattern="$2"
  local message="$3"
  if grep -Eq "$pattern" "$file"; then
    echo "$message"
    exit 1
  fi
}

required_files=(
  PKGBUILD
  daisyos-shell
  daisyos-shell.desktop
  daisyos-shell.install
  daisyos-shell.service
  daisyos-repo.conf
  defaults/settings.json
)

for file in "${required_files[@]}"; do
  if [[ ! -f "$package_dir/$file" ]]; then
    echo "Missing Arch packaging file: packaging/arch/$file"
    exit 1
  fi
done

if command -v makepkg >/dev/null 2>&1; then
  (
    cd "$package_dir"
    makepkg --printsrcinfo >/dev/null
  )
else
  echo "makepkg is unavailable; PKGBUILD metadata validation was skipped."
fi

if command -v shellcheck >/dev/null 2>&1; then
  shellcheck "$package_dir/daisyos-shell" "$package_dir/daisyos-shell.install"
else
  echo "ShellCheck is unavailable; packaging script lint was skipped."
fi

project_version="$({
  sed -n 's/.*<VersionPrefix[^>]*>\([^<]*\)<\/VersionPrefix>.*/\1/p' \
    "$repo_root/Directory.Build.props"
} | head -n 1)"
package_version="$({
  sed -n 's/^pkgver=//p' "$package_dir/PKGBUILD"
} | head -n 1)"
if [[ -z "$project_version" || "$package_version" != "$project_version" ]]; then
  echo "PKGBUILD version '$package_version' does not match project version '$project_version'."
  exit 1
fi

# Keep package ownership and configuration policy executable rather than only
# documenting it. These paths are intentionally exact so accidental moves fail
# review before a package is built or installed.
require_text "$package_dir/PKGBUILD" \
  "backup=\\('etc/daisyos/settings\\.json'\\)" \
  "Administrator settings must be declared as a Pacman backup file."
require_text "$package_dir/PKGBUILD" \
  "\\\$pkgdir/usr/lib/daisyos" \
  "The self-contained shell must be owned under /usr/lib/daisyos."
require_text "$package_dir/PKGBUILD" \
  "\\\$pkgdir/usr/bin/daisyos-shell" \
  "The stable /usr/bin/daisyos-shell launcher is missing."
require_text "$package_dir/PKGBUILD" \
  "\\\$pkgdir/usr/lib/systemd/user/daisyos-shell\\.service" \
  "The systemd user service package path is missing."
require_text "$package_dir/PKGBUILD" \
  "\\\$pkgdir/usr/share/daisyos/defaults/settings\\.json" \
  "Vendor settings must be installed under /usr/share/daisyos/defaults."
require_text "$package_dir/PKGBUILD" \
  "\\\$pkgdir/etc/daisyos/settings\\.json" \
  "Administrator settings must be installed under /etc/daisyos."

if awk '
  /<<'\''EOF'\''/ { in_message = 1; next }
  in_message && /^EOF$/ { in_message = 0; next }
  !in_message && /^[[:space:]]*systemctl[[:space:]].*(enable|start)/ { found = 1 }
  END { exit found ? 0 : 1 }
' "$package_dir/daisyos-shell.install"; then
  echo "Package hooks must not enable or start the shell automatically."
  exit 1
fi
forbid_text "$package_dir/daisyos-shell.install" \
  '(useradd|groupadd|loginctl[[:space:]]+enable-linger)' \
  "Package hooks must not create accounts or change login policy."
require_text "$package_dir/daisyos-shell.service" \
  '^Restart=on-failure$' \
  "The shell service must restart only after failures."
require_text "$package_dir/daisyos-shell.service" \
  '^StartLimitBurst=[1-9][0-9]*$' \
  "The shell service must limit rapid restart loops."
require_text "$package_dir/daisyos-shell.service" \
  '^PartOf=graphical-session\.target$' \
  "The shell service must stop with the graphical session."

profile_packages() {
  sed '/^[[:space:]]*#/d; /^[[:space:]]*$/d' "$1" | sort
}

validate_profile() {
  local profile="$1"
  local packages duplicates
  packages="$(profile_packages "$profile")"
  duplicates="$(printf '%s\n' "$packages" | uniq -d)"
  if [[ -n "$duplicates" ]]; then
    echo "Duplicate packages in ${profile#"$repo_root/"}:"
    printf '%s\n' "$duplicates"
    exit 1
  fi
  if ! grep -Fxq daisyos-shell <<<"$packages"; then
    echo "Profile ${profile#"$repo_root/"} does not install daisyos-shell."
    exit 1
  fi
}

minimal="$repo_root/os/packages/minimal.txt"
desktop="$repo_root/os/packages/desktop.txt"
gaming="$repo_root/os/packages/desktop-gaming.txt"
for profile in "$minimal" "$desktop" "$gaming"; do
  validate_profile "$profile"
done

if missing="$(comm -23 <(profile_packages "$minimal") <(profile_packages "$desktop"))" && [[ -n "$missing" ]]; then
  echo "Desktop profile is missing minimal packages:"
  printf '%s\n' "$missing"
  exit 1
fi

if missing="$(comm -23 <(profile_packages "$desktop") <(profile_packages "$gaming"))" && [[ -n "$missing" ]]; then
  echo "Gaming profile is missing desktop packages:"
  printf '%s\n' "$missing"
  exit 1
fi

require_text "$package_dir/daisyos-repo.conf" \
  '^SigLevel[[:space:]]*=[[:space:]]*Required[[:space:]]+DatabaseRequired$' \
  "The repository must require signatures for packages and its database."
forbid_text "$package_dir/daisyos-repo.conf" \
  '(TrustAll|Never)' \
  "The repository must not bypass signature trust checks."

if grep -Eq "license=\(['\"]MIT['\"]\)" "$package_dir/PKGBUILD" \
    && [[ ! -f "$repo_root/LICENSE" ]]; then
  echo "PKGBUILD declares MIT but the repository has no LICENSE file."
  exit 1
fi

if grep -Eq "license=\(['\"]custom['\"]\)" "$package_dir/PKGBUILD"; then
  add_release_blocker "Choose a redistribution license and install its text."
fi

if grep -Eq "sha256sums=.*['\"]SKIP['\"]" "$package_dir/PKGBUILD"; then
  add_release_blocker "Replace every SKIP source checksum with a reviewed SHA-256 value."
fi

if grep -Eq '^Server[[:space:]]*=.*\.example(/|$)' "$package_dir/daisyos-repo.conf"; then
  add_release_blocker "Replace the example repository address with the reviewed HTTPS release host."
fi

if [[ -n "$report_path" ]]; then
  mkdir -p "$(dirname "$report_path")"
  {
    echo "# DaisyOS Arch package readiness"
    echo
    echo "Project/package version: $project_version"
    echo
    echo "Repository checks: Pass"
    echo "Ownership and config preservation: Pass"
    echo "Service safety policy: Pass"
    echo "Package/database signature policy: Pass"
    echo
    if ((${#release_blockers[@]} == 0)); then
      echo "Release readiness: Ready for a clean disposable build"
    else
      echo "Release readiness: Blocked"
      echo
      echo "Publication blockers:"
      for blocker in "${release_blockers[@]}"; do
        printf -- '- %s\n' "$blocker"
      done
    fi
  } > "$report_path"
  echo "Packaging readiness report written to ${report_path#"$repo_root/"}"
fi

if [[ "$release_mode" == true && ${#release_blockers[@]} -gt 0 ]]; then
  echo "Arch package publication is blocked:"
  for blocker in "${release_blockers[@]}"; do
    printf -- '- %s\n' "$blocker"
  done
  exit 1
fi

echo "Arch packaging checks passed."
