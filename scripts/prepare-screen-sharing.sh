#!/usr/bin/env bash
set -Eeuo pipefail

if [[ "${1:-}" == --help ]]; then
  echo 'Usage: scripts/prepare-screen-sharing.sh'
  echo 'Prepare DaisyOS desktop permissions, authentication and screen capture.'
  exit 0
fi
[[ $# -eq 0 ]] || { echo 'Unexpected argument.' >&2; exit 2; }
desktop="${XDG_CURRENT_DESKTOP:-}"
case ":${desktop,,}:" in
  *:daisyos:*) ;;
  *) echo 'Desktop portal preparation requires the DaisyOS session environment.' >&2; exit 1 ;;
esac
[[ "${XDG_SESSION_TYPE:-}" == wayland && -n "${XDG_RUNTIME_DIR:-}" && -n "${WAYLAND_DISPLAY:-}" ]] || {
  echo 'A running Wayland session is required.' >&2; exit 1;
}
case "$WAYLAND_DISPLAY" in
  /*) wayland_socket="$WAYLAND_DISPLAY" ;;
  *) wayland_socket="$XDG_RUNTIME_DIR/$WAYLAND_DISPLAY" ;;
esac
[[ -S "$wayland_socket" ]] || { echo 'The session Wayland socket is not ready.' >&2; exit 1; }
export GTK_USE_PORTAL=1 GIO_USE_PORTALS=1 QT_QPA_PLATFORMTHEME=xdgdesktopportal
if command -v dbus-update-activation-environment >/dev/null 2>&1; then
  dbus-update-activation-environment --systemd WAYLAND_DISPLAY XDG_RUNTIME_DIR XDG_SESSION_TYPE XDG_CURRENT_DESKTOP GTK_USE_PORTAL GIO_USE_PORTALS QT_QPA_PLATFORMTHEME
fi
systemctl --user cat daisyos-desktop-services.service >/dev/null || {
  echo 'Install the DaisyOS desktop services before preparing the session.' >&2; exit 1;
}
previous_agent=false
if systemctl --user is-active --quiet plasma-polkit-agent.service; then
  previous_agent=true
  systemctl --user stop plasma-polkit-agent.service
fi
if ! systemctl --user restart daisyos-desktop-services.service; then
  if [[ "$previous_agent" == true ]]; then systemctl --user start plasma-polkit-agent.service; fi
  echo 'DaisyOS desktop services failed to start. Check the user service journal.' >&2
  exit 1
fi
capture_types() {
  timeout 5 busctl --user get-property "$1" /org/freedesktop/portal/desktop "$2" AvailableSourceTypes 2>/dev/null
}
has_monitor_capture() {
  local signature value
  read -r signature value <<< "$1"
  [[ "$signature" == u && "$value" =~ ^[0-9]+$ ]] && (( (value & 1) != 0 ))
}
if ! has_monitor_capture "$(capture_types org.freedesktop.impl.portal.desktop.daisyos org.freedesktop.impl.portal.ScreenCast || true)"; then
  echo 'The DaisyOS screen-sharing service is unavailable.' >&2; exit 1
fi
# Recreate the frontend so an already-running process cannot retain KDE routes.
systemctl --user restart xdg-desktop-portal.service
if ! has_monitor_capture "$(capture_types org.freedesktop.portal.Desktop org.freedesktop.portal.ScreenCast || true)"; then
  systemctl --user restart xdg-desktop-portal.service
fi
if ! has_monitor_capture "$(capture_types org.freedesktop.portal.Desktop org.freedesktop.portal.ScreenCast || true)"; then
  echo 'The public portal does not expose DaisyOS screen sharing. Check portal routing.' >&2; exit 1
fi
# Other desktops keep their installed backends; DaisyOS does not start their UI.
systemctl --user stop plasma-xdg-desktop-portal-kde.service xdg-desktop-portal-gtk.service 2>/dev/null || true
echo 'DaisyOS desktop permissions, file selection and screen sharing are available. Reopen applications that cached the previous file picker.'
