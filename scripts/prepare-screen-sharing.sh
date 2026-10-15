#!/usr/bin/env bash
set -Eeuo pipefail

if [[ "${1:-}" == --help ]]; then
  echo 'Usage: scripts/prepare-screen-sharing.sh'
  echo 'Prepare or repair screen capture in the current DaisyOS Wayland session.'
  exit 0
fi
[[ $# -eq 0 ]] || { echo 'Unexpected argument.' >&2; exit 2; }
desktop="${XDG_CURRENT_DESKTOP:-}"
case ":${desktop,,}:" in
  *:daisyos:*) ;;
  *) echo 'Screen capture preparation requires the DaisyOS session environment.' >&2; exit 1 ;;
esac
[[ "${XDG_SESSION_TYPE:-}" == wayland && -n "${XDG_RUNTIME_DIR:-}" && -n "${WAYLAND_DISPLAY:-}" ]] || {
  echo 'A running Wayland session is required.' >&2; exit 1;
}
case "$WAYLAND_DISPLAY" in
  /*) wayland_socket="$WAYLAND_DISPLAY" ;;
  *) wayland_socket="$XDG_RUNTIME_DIR/$WAYLAND_DISPLAY" ;;
esac
[[ -S "$wayland_socket" ]] || { echo 'The session Wayland socket is not ready.' >&2; exit 1; }

# KDE's backend conditionally constructs its capture interfaces only when its
# own desktop is exactly KDE. Scope that compatibility value to this service;
# apps and the portal frontend continue to identify the session as DaisyOS.
dropin_dir="${XDG_CONFIG_HOME:-$HOME/.config}/systemd/user/plasma-xdg-desktop-portal-kde.service.d"
mkdir -p "$dropin_dir"
staged="$(mktemp "$dropin_dir/.daisyos-capture.XXXXXX")"
trap 'rm -f -- "$staged"' EXIT
cat > "$staged" <<'DROPIN'
[Service]
Environment=XDG_CURRENT_DESKTOP=KDE
Environment=QT_QPA_PLATFORM=wayland
DROPIN
changed=false
if ! cmp -s "$staged" "$dropin_dir/50-daisyos-capture.conf"; then
  mv -f -- "$staged" "$dropin_dir/50-daisyos-capture.conf"
  changed=true
fi
if [[ "$changed" == true ]]; then systemctl --user daemon-reload; fi

capture_types() {
  timeout 5 busctl --user get-property "$1" /org/freedesktop/portal/desktop "$2" AvailableSourceTypes 2>/dev/null
}
has_monitor_capture() {
  local signature value
  read -r signature value <<< "$1"
  [[ "$signature" == u && "$value" =~ ^[0-9]+$ ]] && (( (value & 1) != 0 ))
}
backend_restarted=false
systemctl --user start plasma-xdg-desktop-portal-kde.service
if ! has_monitor_capture "$(capture_types org.freedesktop.impl.portal.desktop.kde org.freedesktop.impl.portal.ScreenCast || true)"; then
  systemctl --user restart plasma-xdg-desktop-portal-kde.service
  backend_restarted=true
fi
if ! has_monitor_capture "$(capture_types org.freedesktop.impl.portal.desktop.kde org.freedesktop.impl.portal.ScreenCast || true)"; then
  echo 'KDE portal backend does not expose monitor capture.' >&2; exit 1
fi
systemctl --user start xdg-desktop-portal.service
if [[ "$backend_restarted" == true ]] || ! has_monitor_capture "$(capture_types org.freedesktop.portal.Desktop org.freedesktop.portal.ScreenCast || true)"; then
  systemctl --user restart xdg-desktop-portal.service
fi
if ! has_monitor_capture "$(capture_types org.freedesktop.portal.Desktop org.freedesktop.portal.ScreenCast || true)"; then
  echo 'The public desktop portal does not expose monitor capture. Check DaisyOS portal routing.' >&2; exit 1
fi
echo 'DaisyOS screen capture is available. Capture applications opened before repair may need reopening.'
