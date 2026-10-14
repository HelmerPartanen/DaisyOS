# DaisyOS Development Login Session

The development session makes DaisyOS selectable from GDM, SDDM, and other
Wayland-aware display managers as **DaisyOS (Windowed Prototype)**. It starts
KWin Wayland, installs the session-local DaisyOS KWin window bridge, and then
runs the current checkout using Avalonia's explicit native-Wayland backend.
XWayland applications remain supported by KWin.

This is intentionally a visual-development prototype, not the production
DaisyOS session: it remains a fullscreen `xdg_toplevel`. It is labelled this
way so it can never be confused with the future native layer-shell session.

## Install

From the repository root, run:

```bash
sudo scripts/install-development-session.sh
```

The installer adds these system files:

```text
/usr/share/wayland-sessions/daisyos-development.desktop
/usr/local/bin/daisyos-development-session
/etc/daisyos/daisyos-development-session.conf
```

Sign out. In the display manager's session menu, choose **DaisyOS
(Windowed Prototype)** and sign in normally. The launcher reads the checkout path
from the configuration file, so edit and rebuild the source tree in place;
sign out and back in to exercise a new shell build.

To register a checkout in another location, pass it explicitly:

```bash
sudo scripts/install-development-session.sh --repo /path/to/DaisyOS
```

## Requirements and recovery

The login needs `kwin_wayland`, XWayland, `kpackagetool6`, `qdbus6`, and the .NET SDK. If any are missing,
or the shell fails to start, DaisyOS writes details to:

```text
~/.local/state/DaisyOS/development-session.log
```

Return to the display manager and choose your normal Plasma, GNOME, or other
desktop session. The launcher attempts one bounded shell restart, then ends the
development session rather than entering a crash loop. KWin is never configured
to exit merely because DaisyOS stops.

## Validation

```bash
scripts/test-development-session-install.sh
```

This stages the display-manager entry in a temporary directory and verifies
that the launcher resolves the configured checkout without changing the host.
