# Third-party caption controls and Release selection — 2026-11-13

## Cause and fix

KWin was already using `org.daisyos.decoration`. The close-only screenshot is
consistent with an app-drawn GTK header bar, whose controls are managed by GTK
rather than painted by KWin. The desktop's GNOME button preference was
`appmenu:close`, and the DaisyOS settings portal did not publish
`org.gnome.desktop.wm.preferences/button-layout`. With `GTK_USE_PORTAL=1`, a
fresh GTK 4 process continued to report `menu:close` even after updating the
local GTK files and GNOME preference.

DaisyOS now publishes `:minimize,maximize,close` through the portal and keeps
GTK 3/4 fallback files and the GNOME preference consistent when applying a
system theme. Existing font/theme settings and unrelated INI sections are
preserved. Native KWin caption controls remain enabled; GTK/libadwaita apps
with custom header bars continue to paint their own controls. Dialogs and
apps with explicit header-bar overrides may legitimately show fewer actions.

The GTK setting and app override behavior are documented in
[GtkSettings](https://docs.gtk.org/gtk4/property.Settings.gtk-decoration-layout.html)
and [GtkHeaderBar](https://docs.gtk.org/gtk4/property.HeaderBar.decoration-layout.html).

## Applied and verified

- User GTK 3/4 settings and the GNOME preference were updated, with previous
  settings retained under `artifacts/window-decoration-layout-2026-11-13/`.
- The desktop-services backend was reloaded. The frontend's Settings.Read now
  returns the three-button layout, and fresh GTK 3 and GTK 4 processes both
  report that effective layout.
- A temporary resizable GTK 4 application window displayed mapped minimize,
  maximize and close icons. It closed only its own window. The script and raw
  result are retained in the same artifacts directory.
- 10 focused Release tests passed: theme preference commands, INI preservation
  and portal settings/filter behavior. `git diff --check` passed.

## Release configuration

The user authorized and authenticated installation of the prepared configuration
at `/etc/daisyos/daisyos-native-development.conf`. It now selects `Release`,
preserving the checkout path and `DAISYOS_GLASS=off`; ownership is root:root and
mode 0644. The previous configuration is retained at
`artifacts/performance-2026-11-13/native-development-before-release.conf`.

The desktop-services backend now executes the Release shell binary (PID 84086
at verification). The main shell remains the existing Debug process (PID 4736),
and KWin remains PID 4013. The running supervisor binds its executable before
its restart loop, so changing the configuration requires a new login to switch
the main desktop to Release. No logout or compositor restart was performed.

A later login activated the prepared Release configuration. During the subsequent
authentication/border update the active Release shell was reloaded while KWin
PID 3148 remained running.
