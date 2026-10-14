# Shell

The shell MVP is a fullscreen Avalonia desktop-style application.

Implemented views:

- `ShellView`
- `DesktopView`
- `SystemBarView`
- `DockView`
- `LauncherView`
- `SettingsView`
- `PowerMenuView`
- `NotificationCenterView`

Implemented development shortcuts:

- `Escape`: close open overlays.
- `Ctrl+Q`: quit the shell.

The first shell is intentionally mock-first. Power actions do not affect the host in development mode.

## State and error presentation

Every primary surface must distinguish these states instead of leaving a blank
panel:

- **Loading:** keep the surface responsive and show bounded progress without
  implying that work has completed.
- **Empty:** explain that the operation succeeded but there is nothing to show,
  such as an empty folder, no notifications, or no search results.
- **Offline:** explain that no network is connected and keep connection controls
  available.
- **Unavailable:** explain that the capability could not be read or is not
  installed; do not present this as merely offline.
- **Error:** show a short recovery action inline or in a toast. Never display raw
  exception text, command output, service names, or stack details.

Error and critical toasts bypass Do Not Disturb and gaming quiet mode. Normal
notifications honor toast visibility, Do Not Disturb, and gaming notification
settings. Internal details belong only in private logs or an explicitly opened
diagnostics view.
