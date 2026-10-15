# Screen recording and screen sharing

DaisyOS uses KWin for compositor capture, KDE's portal backend for permission and source selection, and PipeWire for delivering frames to applications. OBS and Discord need a working `org.freedesktop.portal.ScreenCast` interface; a running portal process alone is insufficient.

## Session integration

KDE's backend constructs its ScreenCast, Screenshot, and RemoteDesktop interfaces only when its own `XDG_CURRENT_DESKTOP` is exactly `KDE`. Launching it with `DaisyOS` leaves those interfaces absent, even when Qt correctly connects to Wayland. See the [upstream backend initialization](https://github.com/KDE/xdg-desktop-portal-kde/blob/master/src/desktopportal.cpp).

DaisyOS sets `XDG_CURRENT_DESKTOP=KDE` and `QT_QPA_PLATFORM=wayland` **only for `plasma-xdg-desktop-portal-kde.service`**. The shell, applications, and public portal frontend retain the DaisyOS identity. `packaging/xdg-desktop-portal/daisyos-portals.conf` selects KDE for capture. Native/prototype session installers and ISO staging include these configurations.

The development session prepares the backend after its Wayland socket exists. The preparation checks actual monitor capture capability and repairs an already-running backend/frontend when necessary. Repeating it against a working portal preserves existing streams. It does not restart PipeWire or WirePlumber.

For an existing DaisyOS Wayland login:

```sh
scripts/prepare-screen-sharing.sh
scripts/check-compositor-session.sh --strict
```

The repair installs a user-level backend service drop-in so it survives logout. It needs no administrator access. Reopen applications that cached the previously empty capture-source list.

## Applications

In OBS, add **Screen Capture (PipeWire)** or **Window Capture (PipeWire)**. Camera and V4L2 sources do not capture the desktop. The portal should ask which screen or window to share.

Discord's screen-sharing flow uses the same capture infrastructure. Reopen Discord after repairing the portal if its source list was cached. The application's sharing UI and a live call remain separate verification steps from portal capability detection.

Screen selection and permission remain required. A locked desktop must be unlocked normally before completing an interactive capture test.

## Validation on 2026-10-15

- Before repair, the KDE backend lacked `org.freedesktop.impl.portal.ScreenCast`, the public portal advertised source types `0`, and existing OBS logs reported `No capture sources available`.
- After the backend-only environment fix, the public portal advertised source types `7` (monitor, window, and virtual). Its frontend retained `XDG_CURRENT_DESKTOP=DaisyOS`; the backend used `KDE`.
- The installed Flatpak OBS loaded `linux-pipewire.so` and logged both `Monitor source` and `Window source`. Flatpak reset the requested temporary config environment, so this launch used the existing OBS configuration. No sources were added and no recording or broadcast was started; the test process was closed afterward.
- A real portal client completed `CreateSession` and `SelectSources`. The desktop had locked before the screen picker could be approved, so `Start` timed out. No successful recorded frames or live Discord call are claimed. The test session was closed, and the lock was left intact.
- The strict session check passed with zero failures/warnings. Its new capture gate rejects an active portal advertising no monitor sources.
- Three isolated setup regressions, the compositor checker suite, native/prototype installer staging tests, and the existing session supervisor suite passed. ShellCheck, shell syntax, and whitespace checks passed for the changed shell scripts.

Local evidence is in `artifacts/screen-sharing-validation/`. A next-login physical-session test and an approved OBS recording/Discord share remain outstanding; no ISO was built or booted in this validation.
