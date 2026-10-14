# Native busy cursors

DaisyOS replaces KWin's bouncing startup icon with the selected cursor theme's
animated `progress` cursor (arrow and spinner). A task that prevents interaction
can request `wait` (spinner). The native DaisyOS themes supply both from the provided animated assets;
user cursor preferences remain effective. See [native cursor themes](native-cursor-themes.md).

The native `daisyos-busy-cursor-v1` KWin effect observes compositor startup-added and
startup-removed signals. Stock `startupfeedback` remains loaded for startup
tracking, including XWayland, with its icon feedback disabled in `klaunchrc`.
DaisyOS's launch controller also observes external launch errors and new native
windows. Settings, Notes and Files launches use the same controller, including
activation of existing windows.

Other asynchronous work uses a disposable `DaisyOS.System.Processes.BusyCursor`
lease:

```csharp
using var cursor = BusyCursor.Begin(); // progress; interaction remains available
await operation(cancellationToken);
// using also releases feedback when the operation throws or is cancelled.
```

Use `Begin(blocking: true)` for a task that actually prevents interaction. Do not
wrap idle polling or the lifetime of a dialog. Integrated task feedback covers
Files directory loading, transfers and extraction, Settings storage scanning,
and App Store package operations.

The UI never waits for cursor IPC. Each scope has its own session-bus connection
and request ID; only its owner can release it. The compositor tracks concurrent
leases independently, delays feedback 150 ms, expires abandoned requests after
15 seconds, and releases requests when their caller disconnects. Live long tasks
renew their lease every 5 seconds. Feedback errors never fail the task.

The effect changes KWin's actual cursor source without intercepting pointer or
keyboard events or drawing an overlay. KWin's `ShapeCursorSource` supplies native
animation, theme fallbacks, hotspot, scale and output handling. Feedback yields
to lock screens, hidden cursors, drag and drop, resizing, selection and active
full-screen effects. The latest client cursor is restored on completion. Idle
cursor animation and polling timers stop.

## Build, install and verify

```bash
scripts/install-busy-cursor.sh
scripts/test-busy-cursor-runtime.py --expiry
```

Requires matching KWin development headers, Qt 6, ECM, Wayland, Vulkan headers,
Ninja and CMake. The build helper also recognizes the existing pinned frame-effect
ECM and Vulkan header cache. KWin binary extensions must be rebuilt against the
target KWin ABI. Native Development installs the effect before starting KWin;
publishing includes it and ISO staging installs it with compositor defaults.
Updating an already loaded binary takes effect on the next session login; a
shell-only restart does not replace libraries mapped into KWin.

The runtime test verifies animated frames from the real compositor cursor source,
progress/wait shapes, presentation delay, overlapping operations, caller isolation,
disconnect cleanup, restoration, stopped idle animation and abandoned-task expiry.
A physical multi-monitor pointer/scale inspection remains separate from these
protocol and animation checks.

Implementation follows the compositor's native [cursor source implementation](https://github.com/KDE/kwin/blob/master/src/cursorsource.cpp) and [startup feedback signals](https://github.com/KDE/kwin/blob/master/src/plugins/startupfeedback/startupfeedback.cpp).

Verified on 2026-10-13: native and shell builds, two focused launch tests, live
cursor lifecycle tests including the 15-second watchdog, a compositor screenshot
showing progress feedback, and a Settings launch with feedback clearing on window
activation. Physical mixed-scale multi-monitor checks and ISO boot are not part
of this validation.
