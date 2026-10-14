# Compositor and Wayland Integration

## Status and Decision

DaisyOS uses **KWin Wayland** as its production compositor target. The live-ISO
profile now starts KWin directly on DRM with XWayland for the current Avalonia
shell and normal applications. If direct DRM composition cannot initialize, it
starts KWin nested over the validated Xorg server with software composition.
The original compositor-free Xorg shell remains only as the final recovery
path and is rejected by normal ISO acceptance checks.

The native development display-manager path uses Avalonia's explicit Wayland
backend, the maintained DaisyOS layer-shell extension, and a KWin-script bridge
for compositor-authoritative application windows. It creates native desktop,
panel, and overlay surfaces; it refuses to fall back to a fullscreen
`xdg_toplevel` when layer-shell is unavailable. Settings and Files remain normal
compositor-managed windows.

This distinction is important: the current lock overlay protects the prototype
UI, but only a compositor/login-manager lock path can secure a real session.

The desktop shell does not use compositor or shader blur. Shell chrome,
Settings, and Files use clean, crisp semantic materials and solid/translucent
styling without requiring KWin blur regions or GPU blur shaders.

## Why KWin

KWin already owns the difficult desktop responsibilities DaisyOS needs:

- Wayland input and output management
- normal application windows, focus, move, resize, snapping, and workspaces
- XWayland for legacy applications
- multi-monitor layout and fractional scaling
- variable refresh rate and frame scheduling
- color-management and HDR evolution
- window decorations, effects, and blur
- screen capture mediation through desktop portals

DaisyOS should integrate with those capabilities rather than implement a
compositor in Avalonia.

## Compositor Package Set

The core runtime packages are now included in the default live profile at
`os/archiso/DaisyOS/packages.x86_64`. The larger list in
`os/packages/kwin-wayland-prototype.txt` remains the development and acceptance
tooling reference.

`qt6-wayland` is not included. On current Arch releases the Qt 6 Wayland client
plugin is part of `qt6-base`; `qt6-wayland` is for applications implementing a
Qt Wayland compositor.

## Session Architecture

```text
login manager or VM autologin
  -> D-Bus user session
    -> KWin Wayland
      -> XWayland (compatibility)
      -> desktop portals
      -> DaisyOS shell surfaces
      -> normal Linux applications
```

The windowed prototype currently uses:

```text
current developer session
  -> nested kwin_wayland window
  -> DaisyOS native Wayland shell client and KWin window bridge
```

Run it with `scripts/run-kwin-prototype.sh`. The script refuses to run without
an existing graphical session, so it cannot accidentally become the host
desktop compositor. For the direct DRM development login workflow, install and
select **DaisyOS (Native Development)** as documented in
`docs/native-development-session.md`; it remains opt-in until the acceptance
gates below pass on the target hardware.

Run `scripts/test-kwin-nested-runtime.sh` for the bounded automated acceptance
path. It binds the strict report to the exact nested KWin process, proves the
nested socket accepts a native Wayland client, exercises an XWayland client,
and checks that the original host display still answers after nested KWin
exits. The production `DaisyOS` entry is deliberately not installed by this
script. It will be installed only with the version-pinned
`DaisyOS.Avalonia.Wayland` backend, which binds `zwlr_layer_shell_v1` before
creating any shell surface. If that backend or protocol is unavailable it exits
with a clear error instead of falling back to this fullscreen client.

## Surface Ownership

| DaisyOS surface | Windowed prototype | Native DaisyOS role |
|---|---|---|
| Wallpaper and desktop | One fullscreen Avalonia window | Background layer per output |
| System bar | Inside fullscreen window | Primary-output panel layer, exclusive bottom edge |
| Dock | Inside fullscreen window | Primary-output panel layer, exclusive bottom edge |
| Launcher and search | Owned native surfaces | Overlay layer or activated popup |
| Notifications | Inside fullscreen window | Overlay/top layer, no keyboard focus by default |
| Settings and Files | Normal Avalonia windows | `xdg_toplevel` normal windows |
| Lock screen | Visual overlay only | Compositor/login-manager lock integration |

### Layer-shell boundary

`zwlr_layer_shell_v1` can anchor a client surface to output edges, assign its
desktop layer, and reserve an exclusive zone. Avalonia's stock backend creates
normal `xdg_toplevel` windows. DaisyOS therefore has a strict shell-surface
contract (`LayerShellSurfaceOptions`) and refuses to create a fullscreen
`xdg_toplevel` as a production fallback. The pinned backend must bind the
protocol before it constructs background, primary-panel, or overlay surfaces.

## Window and Application Integration

The KWin integration layer now publishes stable identifiers and lifecycle
snapshots for normal compositor windows. The remaining extension points are:

- created, closed, focused, minimized, maximized, and fullscreen windows
- application ID, title, icon, output, and workspace
- activate, minimize, maximize, close, and move-to-workspace requests
- thumbnail or screencopy handles only after user permission

KWin scripting or a reviewed D-Bus/plugin bridge is preferred over parsing
process lists. DaisyOS must not expose force-close as the normal close action.

## Window Decorations

Native Development uses the version-matched `org.daisyos.decoration`
KDecoration3 plugin for applications that request server-side decoration. It
exports the Settings window's semantic DaisyTheme roles through
`daisyos-window-decorationrc`, so light/dark changes repaint KWin decorations
without restarting applications. DaisyOS-owned Avalonia windows continue to use
their client-side Settings chrome and must not receive a second frame.

Third-party applications remain authoritative when they draw client-side
decorations. DaisyOS must not promise identical title bars for GTK, Qt, Electron,
games, XWayland, and custom-rendered applications. The acceptance target is
cohesive server-side decoration where supported, correct native client behavior
everywhere else, and no input-region mismatch around resize borders.

## Required Protocols and Services

### Required for the first usable session

- `xdg_wm_base` for normal application windows
- `wl_output`/output management exposed through KWin and system settings
- `xdg_activation_v1` for focus-safe application activation
- XWayland for applications without native Wayland support
- `xdg-desktop-portal-kde` for screenshots, screen sharing, file dialogs, and
  other permission-mediated desktop operations
- PipeWire for portal-mediated screen capture and normal audio

### Required before replacing the Xorg fallback

- shell surface placement and exclusive zones
- secure session locking
- global shortcut ownership without shortcut conflicts
- output add/remove, fractional scaling, and mixed-refresh testing
- clipboard and drag/drop behavior across native Wayland and XWayland apps
- notification and popup focus rules

### Later gates

- color management and ICC profiles
- HDR metadata and tone-mapping policy
- VRR policy per display and fullscreen window
- screenshot and recording indicators
- input-method and accessibility protocol integration

## Multi-monitor Rules

- Create one background surface per output; never stretch one bitmap across all
  outputs by default.
- Keep one primary system bar and Dock initially. A later setting may mirror them.
- Re-home menus and notifications when their output disappears.
- Preserve logical coordinates across mixed scale factors.
- Test 100%, 125%, 150%, and 200% scale combinations with hot-plug.
- Never place recovery dialogs solely on a disconnected output.

## Gaming and Creative Workloads

- Do not add an extra compositing layer around fullscreen games.
- Verify direct scan-out, VRR, frame pacing, and XWayland fullscreen behavior.
- Do not force global color conversion when an application owns an HDR or
  color-managed surface.
- Keep screen recording portal-mediated and visible to the user.
- Measure latency and frame-time variance before enabling visual effects by
  default in gaming mode.

## Failure and Recovery

The production session must provide:

1. a restart-limited shell unit so repeated crashes do not loop forever;
2. KWin logs and shell logs in the user journal;
3. a VM-tested fallback entry that starts the Xorg prototype;
4. a keyboard-accessible recovery choice before automatic login;
5. plain UI wording: “The desktop couldn’t start. Try the fallback session.”

The shell crashing must not terminate KWin or other applications. The
display-manager development session uses one bounded shell restart and then
returns control to the display manager; it does not use `--exit-with-session`.

## Verification Gates

The executable acceptance cases and per-environment evidence record live in
`docs/compositor-test-matrix.md`. Runtime rows stay unverified until they are
performed in the named nested or QEMU environment.

### Gate 1: repository validation

- prototype package list contains the required packages and no duplicates;
- scripts pass `shellcheck`;
- shell publishes successfully.

Run `scripts/check-kwin-prototype.sh`.

Inside a running test session, capture a read-only environment report with
`scripts/check-compositor-session.sh --strict --output artifacts/compositor/session-report.md`.

### Gate 2: nested developer session

- shell starts inside nested KWin;
- native Wayland and XWayland test applications open;
- Settings and Files remain normal windows;
- keyboard, pointer, clipboard, audio, and app launching work;
- closing the shell closes only the nested session.

### Gate 3: QEMU live ISO, opt-in session

- KWin starts on the virtual GPU without software-render crash loops;
- shell restart and journal recovery work;
- 1x and 1.25x scaling are usable;
- hot-plug simulation and resolution changes preserve accessible UI;
- fallback Xorg session remains selectable.

### Gate 4: default-session review

KWin Wayland may replace Xorg only after all prior gates pass on Intel, AMD,
NVIDIA, and QEMU/virtio graphics, with no unresolved shell-locking or recovery
issues.

## Explicit Non-goals

- writing a custom compositor now;
- replacing KWin’s window-placement or rendering engine;
- claiming secure locking from an Avalonia overlay;
- enabling the prototype package set in release ISOs before VM review;
- depending on private KWin interfaces without a compatibility strategy.
