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
sign in. New sessions use the optimized Release shell, building it if missing.
Set `DAISYOS_SHELL_CONFIGURATION=Debug` in the session configuration when debugging
requires it. Restarts rebuild the configuration recorded by the active supervisor;
older supervisors continue using Debug until the next login with the updated
session launcher installed.

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

The installer also configures up to 4 GiB of zstd-compressed zram and, on a real
host installation, a 4 GiB low-priority disk swap file. Zram absorbs short
bursts without storage I/O; the disk file is an emergency fallback for sustained
or poorly compressible pressure. It can increase SSD writes and may make a
severely pressured workload slower, but avoids the no-swap failure cliff.

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

## Screen recording and sharing

The session starts DaisyOS desktop services for authentication, permission dialogs, file selection and compositor capture. See [desktop services](desktop-services.md) for setup and remaining compatibility boundaries. See [screen sharing](screen-sharing.md) for the repair command, OBS sources, and end-to-end verification boundaries.
