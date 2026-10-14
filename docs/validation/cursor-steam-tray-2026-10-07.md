# Cursor settings, Steam launch, and background-app tray — 2026-10-07

## Changes

- Cursor settings persist the KDE theme and size, then emit the cursor-specific
  `org.kde.KGlobalSettings.notifyChange(5, 0)` signal. Generic KWin reconfigure
  does not reload cursor themes.
- The local Avalonia Wayland backend uses `wp_cursor_shape_manager_v1` for
  supported standard cursors. KWin supplies the selected theme and size on
  DaisyOS surfaces; custom bitmap cursors and unsupported shapes/compositors
  retain the existing client cursor path.
- Application launches resolve XWayland from the compositor owning the current
  Wayland socket, including its lazy listening socket and actual authority file.
  They no longer guess the first global X socket or `:0`, and do not mutate the
  shell's process environment. The live session's display is `:1`; `:0` belongs
  to another session.
- System Bar has a Background apps panel backed by real StatusNotifier entries.
  DaisyOS hosts the watcher when absent and can attach to an existing watcher.
  Active/attention entries show app icons and labels through tooltips and
  accessibility names. Left-click activates the app; right-click opens its menu.
  Menu items forward their actions to the owning app. Passive/disconnected
  entries are removed, and icon decoding runs away from the UI thread.
- The upward chevron rotates 180 degrees around its center when opened and
  reverses on dismissal, using the shared slow motion duration and cubic easing.
  Reduced motion uses zero duration. Escape, outside clicks, other flyouts,
  fullscreen, native input ownership, and material blur include the tray.

## Validation

- Initial focused backend checks: seven passed, including a private D-Bus fixture
  for item registration, status refresh, activation, menus, and disconnection.
- Broad headless UI checks: 114 passed before the final additional rotation test.
- Main suite: 375 passed, one failed, one opt-in capture skipped. The failure is
  the previously observed hardcoded icon sizes in WidgetLibraryOverlay.axaml.
- KWin live read-back changed cursor size from 24 to 48 after the signal; the
  previous size was restored. The active KWin advertises cursor-shape support.
- Steam was launched using the corrected SafeProcessLauncher. XWayland and the
  Steam web helper are running, and the X11 window has WM_CLASS `steamwebhelper`,
  `steam` and title `Steam`, rather than the reported X connection error.
- Complete repository gate encounters the existing Avalonia DesignerSupport
  prerequisite failure: `dnx Bun.Unofficial.Tool --yes -- install` exits 127.
- Git status/diff cannot run because the current HEAD object is empty/corrupt.
  Existing files were saved before edits under a task-specific temporary backup;
  source changes are reviewed against that backup.

Final focused checks, deployment, and live tray observations are recorded below.

- Final focused GUI checks: five passed, including a sampled intermediate
  rotation, fixed center, reversal, and synchronous reduced-motion state.
  The light-theme empty tray frame was rendered and inspected.
- Repository formatting reports extensive existing whitespace/import errors.
  New C# files were whitespace-formatted separately; added-line whitespace
  checks against saved originals passed.

- Final focused backend checks: seven passed, including attaching a second host
  to an existing watcher and application-menu initialization before layout.
- Native development rebuild/restart succeeded; KWin PID 1024 and the existing
  Steam application survived the shell replacement. Strict live preflight
  reports zero required failures and zero warnings.
- The restarted shell owns the real watcher and Steam registered
  `/org/ayatana/NotificationItem/steam`. A separate host using the same service
  code found the Steam entry and read its 16 menu entries.
- Live Settings request/reply calls returned `ok` for Breeze_Light at size 32
  and for restoring breeze_cursors at size 24. KWin read-back matched both.
- The project screenshot helper did not return a capture and was terminated.
  A bounded Spectacle capture succeeded instead. The native image was inspected:
  Steam has a normal window; the tray shows Steam's actual monochrome icon in
  its material card and the system-bar chevron points down while open.
  This verifies visible native placement and appearance, not exhaustive physical
  pointer, keyboard, or every application-menu interaction.

- Final service introspection exposes its standard properties interface; live
  qdbus GetAll reads the registered Steam entry and host state successfully.
