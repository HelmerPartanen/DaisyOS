#!/usr/bin/env bash
set -Eeuo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
[[ $# == 2 && "$1" == --root && -n "$2" ]] || {
  echo 'Usage: scripts/stage-gaming-integration.sh --root ROOT' >&2; exit 2;
}
root="${2%/}"
[[ -n "$root" ]] || root=/
install -Dm0644 "$repo_root/scripts/daisyos-gaming-environment" "$root/usr/local/lib/daisyos/gaming-environment"
install -Dm0755 "$repo_root/scripts/daisyos-game-launcher" "$root/usr/local/bin/daisyos-game-launcher"
install -Dm0755 "$repo_root/scripts/daisyos-game" "$root/usr/local/bin/daisyos-game"
install -Dm0755 "$repo_root/scripts/daisyos-gaming-diagnostics" "$root/usr/local/bin/daisyos-gaming-diagnostics"
install -Dm0644 "$repo_root/packaging/gaming/60-daisyos-gamemode.rules" "$root/usr/share/polkit-1/rules.d/60-daisyos-gamemode.rules"
for application in steam lutris; do
  cat > "$root/usr/local/bin/$application" <<LAUNCHER
#!/usr/bin/env bash
exec /usr/local/bin/daisyos-game-launcher /usr/bin/$application "\$@"
LAUNCHER
  chmod 0755 "$root/usr/local/bin/$application"
done
for application in wine wine64; do
  cat > "$root/usr/local/bin/$application" <<'LAUNCHER'
#!/usr/bin/env bash
export WINEDEBUG="${WINEDEBUG:--all}"
export WINEDLLOVERRIDES="${WINEDLLOVERRIDES:-winemenubuilder.exe=d}"
target="/usr/bin/$(basename "$0")"
if [[ ! -x "$target" && -x "$HOME/.local/bin/$(basename "$0")" ]]; then
  target="$HOME/.local/bin/$(basename "$0")"
elif [[ ! -x "$target" ]]; then
  target="$(command -v "$(basename "$0")" || true)"
fi
exec /usr/local/bin/daisyos-game-launcher "$target" "$@"
LAUNCHER
  chmod 0755 "$root/usr/local/bin/$application"
done
install -d "$root/usr/local/share/applications"
cat > "$root/usr/local/share/applications/wine.desktop" <<'DESKTOP'
[Desktop Entry]
Type=Application
Name=Wine Windows Program Loader
Comment=Run Windows applications seamlessly
Exec=/usr/local/bin/wine start /unix %f
MimeType=application/x-ms-dos-executable;application/x-msdownload;application/x-dosexec;application/vnd.microsoft.portable-executable;application/x-msi;application/x-wine-extension-msp;application/x-ms-shortcut;
Icon=wine
Terminal=false
NoDisplay=true
StartupNotify=true
Categories=Utility;
DBusActivatable=false
DESKTOP
chmod 0644 "$root/usr/local/share/applications/wine.desktop"
for application in steam lutris; do
  install -d "$root/usr/local/share/applications"
  desktop_id="$application"
  [[ "$application" != lutris ]] || desktop_id=net.lutris.Lutris
  cat > "$root/usr/local/share/applications/$desktop_id.desktop" <<DESKTOP
[Desktop Entry]
Type=Application
Name=${application^}
Exec=/usr/local/bin/daisyos-game-launcher /usr/bin/$application %U
TryExec=/usr/bin/$application
Icon=$desktop_id
Terminal=false
Categories=Game;
DBusActivatable=false
DESKTOP
  if [[ "$application" == steam ]]; then
    echo 'MimeType=x-scheme-handler/steam;x-scheme-handler/steamlink;' >> "$root/usr/local/share/applications/$desktop_id.desktop"
  else
    echo 'MimeType=x-scheme-handler/lutris;application/x-lutris;' >> "$root/usr/local/share/applications/$desktop_id.desktop"
    echo 'StartupWMClass=net.lutris.Lutris' >> "$root/usr/local/share/applications/$desktop_id.desktop"
    if [[ -f "$root/usr/local/share/applications/lutris.desktop" ]] &&
       grep -Fxq 'Exec=/usr/local/bin/daisyos-game-launcher /usr/bin/lutris %U' "$root/usr/local/share/applications/lutris.desktop"; then
      rm -- "$root/usr/local/share/applications/lutris.desktop"
    fi
  fi
done
