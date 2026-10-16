# Screen recording and screen sharing

DaisyOS uses KWin for compositor capture, DaisyOS's portal backend for permission and source selection, and PipeWire for delivering frames to applications. OBS and Discord need a working `org.freedesktop.portal.ScreenCast` interface; a running portal process alone is insufficient.

## Session integration

DaisyOS owns the selection and consent windows in `daisyos-desktop-services.service`.
The backend uses KWin's restricted screencast transport and PipeWire delivers the
selected streams. Screen and window selection always require consent. See
[desktop services](desktop-services.md) for setup, supported interfaces and
remaining compatibility boundaries.

For an existing development login, build and install the desktop services before
activating the new routing:

```sh
scripts/build-desktop-services.sh
dotnet build src/DaisyOS.Shell/DaisyOS.Shell.csproj --disable-build-servers -m:1
scripts/install-user-desktop-services.sh
scripts/prepare-screen-sharing.sh
scripts/check-compositor-session.sh --strict
```

Preparation restarts the public portal to discard its previous routing, which
ends existing portal sessions. It does not restart PipeWire or WirePlumber.
Reopen applications that cached the previous portal or file-picker environment.

## Applications

In OBS, add **Screen Capture (PipeWire)** or **Window Capture (PipeWire)**. Camera and V4L2 sources do not capture the desktop. The portal should ask which screen or window to share.

Discord's screen-sharing flow uses the same capture infrastructure. Reopen Discord after repairing the portal if its source list was cached. The application's sharing UI and a live call remain separate verification steps from portal capability detection.

Screen selection and permission remain required. A locked desktop must be unlocked normally before completing an interactive capture test.

## Historical KDE-backend validation on 2026-10-15

The following records the previous implementation; it does not validate the new DaisyOS backend.

- Before repair, the KDE backend lacked `org.freedesktop.impl.portal.ScreenCast`, the public portal advertised source types `0`, and existing OBS logs reported `No capture sources available`.
- After the backend-only environment fix, the public portal advertised source types `7` (monitor, window, and virtual). Its frontend retained `XDG_CURRENT_DESKTOP=DaisyOS`; the backend used `KDE`.
- The installed Flatpak OBS loaded `linux-pipewire.so` and logged both `Monitor source` and `Window source`. Flatpak reset the requested temporary config environment, so this launch used the existing OBS configuration. No sources were added and no recording or broadcast was started; the test process was closed afterward.
- A real portal client completed `CreateSession` and `SelectSources`. The desktop had locked before the screen picker could be approved, so `Start` timed out. No successful recorded frames or live Discord call are claimed. The test session was closed, and the lock was left intact.
- The strict session check passed with zero failures/warnings. Its new capture gate rejects an active portal advertising no monitor sources.
- Three isolated setup regressions, the compositor checker suite, native/prototype installer staging tests, and the existing session supervisor suite passed. ShellCheck, shell syntax, and whitespace checks passed for the changed shell scripts.

Local evidence is in `artifacts/screen-sharing-validation/`. A next-login physical-session test and an approved OBS recording/Discord share remain outstanding; no ISO was built or booted in this validation.
