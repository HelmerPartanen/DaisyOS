#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
published_shell="$repo_root/publish/linux-x64/DaisyOS.Shell"
debug_shell="$repo_root/src/DaisyOS.Shell/bin/Debug/net10.0/DaisyOS.Shell"

export DAISYOS_SHELL_SERVICES="${DAISYOS_SHELL_SERVICES:-real}"
export XDG_CURRENT_DESKTOP=DaisyOS
export XDG_SESSION_DESKTOP=DaisyOS
export XDG_SESSION_TYPE=wayland
# The nested prototype must tell us whether KWin has configured the native
# layer surfaces. This is opt-in and never enabled by the display-manager
# session launcher.
export DAISYOS_WAYLAND_DIAGNOSTICS="${DAISYOS_WAYLAND_DIAGNOSTICS:-1}"


printf 'Starting DaisyOS on Wayland socket: %s\n' "${WAYLAND_DISPLAY:-<unset>}"

if [[ -x "$published_shell" && "${DAISYOS_USE_PUBLISHED:-0}" == "1" ]]; then
  exec env DAISYOS_SHELL_SESSION=1 "$published_shell" --real-services --shell-session
fi

if ! command -v dotnet >/dev/null 2>&1; then
  echo "The published shell and dotnet are both unavailable. Run scripts/publish-shell.sh first."
  exit 1
fi

verify_shell_binary() {
  [[ -x "$debug_shell" ]] || return 1
  "$debug_shell" --verify-app-resources >/dev/null 2>&1
}

repair_shell_build() {
  echo "The existing DaisyOS build is incomplete; rebuilding its Avalonia resources..."
  dotnet build "$repo_root/src/DaisyOS.Shell/DaisyOS.Shell.csproj" \
    --no-restore --maxcpucount:1 -t:Rebuild -v:minimal
}

if [[ "${DAISYOS_REBUILD_SHELL:-0}" != "1" && -x "$debug_shell" ]]; then
  if verify_shell_binary; then
    echo "Launching the existing Debug shell build. Set DAISYOS_REBUILD_SHELL=1 to rebuild first."
    exec env DAISYOS_SHELL_SESSION=1 "$debug_shell" --real-services --shell-session
  fi
  echo "Existing Debug shell build is incomplete or failed resource verification."
fi

echo "Building DaisyOS before opening the nested compositor..."
if ! dotnet build "$repo_root/src/DaisyOS.Shell/DaisyOS.Shell.csproj" --no-restore -v:minimal || ! verify_shell_binary; then
  repair_shell_build
fi

if ! verify_shell_binary; then
  echo "DaisyOS could not verify its compiled UI resources."
  exit 1
fi

exec env DAISYOS_SHELL_SESSION=1 "$debug_shell" --real-services --shell-session

