# DaisyOS Gaming Coordinator

`daisy-gamingd` is a per-user Rust service started with
`graphical-session.target`. The Avalonia shell sends the user's three gaming
preferences and manual Gaming Mode toggle over the session bus, then mirrors
the coordinator's `gaming_active` status into the existing desktop state.

## D-Bus API

- Bus name and interface: `org.daisyos.Gaming1`
- Object path: `/org/daisyos/Gaming1`
- `Status() -> s`: JSON containing `settings` and `runtime` snapshots.
- `SetManualEnabled(b enabled, b user_initiated)`: syncs the manual request at
  startup; an explicit user action can dismiss auto mode for the current game.
- `Configure(b automatic, b allow_battery_boost, b performance_profile)`:
  updates only the three supported booleans.

Every method resolves the D-Bus caller's Unix UID and accepts only the UID
running this session daemon. The API accepts no command strings, process IDs,
sysfs paths, governor values, or arbitrary profile names. The daemon runs as
the logged-in user; CPU policy remains with GameMode, and profile changes use
`powerprofilesctl`/Power Profiles Daemon. GameMode's packaged polkit rule
continues to authorize only its governor helper for an active local session.

## Session and profile policy

The daemon scans `/proc` every three seconds for the existing supported Steam
`reaper SteamLaunch AppId=…` and Lutris `lutris-wrapper` signatures, then tracks
their child process trees. Automatic Gaming Mode latches for one detected game.
Turning Gaming Mode off dismisses the automatic latch until that game exits.
Manual Gaming Mode remains enabled across game exit. On battery, GameMode and
the Performance profile are both suppressed unless the user opted in.

When PPD advertises `performance`, the daemon records the previous profile in
a private file under `$XDG_RUNTIME_DIR/daisyos-gamingd/` before changing it.
On game exit, service shutdown, or daemon restart, it restores the snapshot only
if the active profile is still `performance`; a different profile is treated
as a user or platform change and is preserved. A missing/unsupported profile
is a no-op. Thermal controls and CPU sysfs files are never changed directly.

Logs are newline-delimited JSON on stderr and are collected by the user systemd
journal. Game command lines and game names are not logged.

## Packaging

Build the release binary with `scripts/build-daisy-gamingd.sh`. Shell publish
and release scripts bundle it with `daisyos-gamingd.service`; ISO staging and
the Arch package install the user unit and enable it for the graphical session.
The native development session installs the current checkout's binary and
user-session D-Bus activation file with `scripts/install-user-gamingd.sh`.

Runtime acceptance still requires a booted DaisyOS session: verify D-Bus
ownership and same-UID rejection, Steam and Lutris start/exit transitions,
manual dismissal, PPD available/unavailable behavior, user profile changes,
and restart recovery while collecting the user journal.
