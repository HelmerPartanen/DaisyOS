# DaisyOS Native Development Login Session

**DaisyOS (Native Development)** is the real Wayland layer-shell development
session. It starts KWin directly on DRM, creates DaisyOS desktop/chrome/overlay
surfaces through `zwlr_layer_shell_v1`, and leaves normal applications under
KWin ownership. It is intentionally separate from **DaisyOS (Windowed
Prototype)** and from your regular GNOME session.

## Install and start

From the checkout root:

```bash
sudo scripts/install-native-development-session.sh
```

Sign out, select **DaisyOS (Native Development)** in the display manager, and
sign in. The session builds the current Debug shell once during startup.

When firewalld is enabled, the installer permits LocalSend's TCP and UDP port
`53317` on the active LAN zone. LocalSend still accepts incoming files only
while its ten-minute receive window is enabled in Quick Settings.

## Daily edit loop

Make a change, then run this from a terminal in the DaisyOS session:

```bash
daisyos-native-development restart
```

The command builds first. If the build fails, the currently running shell is
not touched. If it succeeds, only DaisyOS restarts; KWin and your Wayland or
XWayland applications remain open. The taskbar reconnects from KWin's current
window snapshot after DaisyOS returns.

Useful diagnostics:

```bash
daisyos-native-development status
daisyos-native-development logs --follow
daisyos-native-development memory status
```

## Memory protection

Native Development reserves memory for KWin, DaisyOS, and essential session
services before application pressure can make the desktop unresponsive. The
reserve scales with installed RAM: 1.25 GiB on 4 GiB machines, 1.5 GiB on 8
GiB, 2 GiB on 16 GiB, 2.5 GiB on 32 GiB, and 3.5 GiB on 64 GiB and above.

Applications launched by DaisyOS are placed in a separate cgroup with an
aggregate `MemoryHigh` boundary below that reserve. At sustained memory
pressure, `systemd-oomd` ends the affected app cgroup before KWin or the shell
become an out-of-memory victim. The installer enables `systemd-oomd` when it
is available; use `daisyos-native-development memory status` to confirm it.

## Recovery behavior

The native session never falls back to a fullscreen Avalonia window: the shell
requires the layer-shell protocol before it creates any desktop surface. If the desktop process exits or is stopped from System Monitor, the
supervisor restarts it without closing application windows. Repeated failures
back off from one second to a maximum of fifteen seconds between attempts;
sixty seconds of desktop uptime resets the failure counter. Explicit logout
stops the session supervisor and ends the compositor session.

The desktop remains visible as a process, like other desktop environments.
Files and Settings run as separate application processes. Process visibility
is intentional and does not determine whether a component is part of the OS.

Logs are written to:

```text
~/.local/state/DaisyOS/native-development-session.log
```

This entry remains experimental until the native compositor acceptance matrix
has passed on the hardware you use every day.

## Performance and memory verification

The measured optimizations, benchmark results, and remaining acceptance limits
are recorded in [Performance and memory verification](architecture/performance-and-memory.md).
