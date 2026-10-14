# Login and Session Management

## Current status

DaisyOS currently runs as a shell inside an existing Linux session. Its lock
screen has a password prompt and uses the configured authentication service, but
it is not a secure compositor lock. The shell overlay cannot prevent another
Wayland or X11 client from appearing above it.

Incorrect passwords are rate-limited. Three failures start a 30-second cooldown
with a visible countdown; the user can retry without restarting the desktop.

## Login-manager decision

The first KWin Wayland image should integrate with SDDM rather than implementing
a greeter or authentication stack in DaisyOS. SDDM already supports selecting
Wayland sessions, PAM integration, multiple users, and session switching. A
DaisyOS greeter theme may change presentation, but authentication and session
creation remain owned by the login manager.

The session package should install a Wayland session entry that starts the
D-Bus user session, KWin, portals, policy agent, and DaisyOS shell in that order.
The existing Xorg entry remains a recovery choice until the Wayland gates pass.

## User-management UX

Settings → Users is the expected home for:

- current account and administrator status;
- changing the current password;
- creating and removing local accounts;
- parental restrictions;
- switching sessions.

These actions remain labelled unavailable until they can use reviewed system
account APIs and polkit authorization. DaisyOS must never edit `/etc/passwd`,
`/etc/shadow`, or PAM configuration directly from the UI. Password fields must
not be logged, persisted in settings, retained after dialogs close, or passed in
command-line arguments.

Deleting an account must show whether its home folder will be retained, require
authentication, and refuse to delete the active account. Session switching must
save no credentials and hand control back to SDDM.

## Production lock requirements

Before calling the lock screen secure:

- use the compositor/session-lock protocol or the supported KWin lock path;
- authenticate through PAM without shelling out or exposing passwords;
- ensure password memory is cleared immediately after each attempt;
- support keyboard layouts, accessibility, fingerprint fallback, and visible
  Caps Lock state;
- keep rate limiting recoverable and avoid revealing whether another account
  exists;
- test suspend/resume, monitor hot-plug, compositor restart, and session switch;
- provide a login-manager recovery route when the shell fails.
