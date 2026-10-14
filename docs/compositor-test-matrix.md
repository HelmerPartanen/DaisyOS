# KWin Wayland session test matrix

This matrix is the acceptance record for Milestone 6. It separates repository readiness from tests that require a real nested session or QEMU image. Do not mark a runtime row as passed from source inspection alone.

## Before each run

1. Build and test the exact revision under review with `scripts/test.sh`.
2. Publish the shell with `scripts/publish-shell.sh`.
3. Start the opt-in nested session with `scripts/run-kwin-prototype.sh`.
4. Inside that session, capture the read-only preflight:

   ```bash
   scripts/check-compositor-session.sh --strict \
     --output artifacts/compositor/session-report.md
   ```

5. Record the GPU, VM image or host distribution, KWin version, Avalonia/.NET build, display layout, and scale factors. Do not include usernames, Wi-Fi names, clipboard contents, or unrelated logs.

Result values are `Pass`, `Fail`, `Blocked`, and `Not run`. A blocked row must name the missing package, protocol, hardware capability, or test environment.

## Gate A: repository and startup

| ID | Test | Expected result | Evidence |
|---|---|---|---|
| A1 | Run `scripts/check-kwin-prototype.sh` | Package list, script lint, and required test tools pass | Command output |
| A2 | Run `scripts/test-compositor-session-check.sh` | Healthy and broken fixture sessions are classified correctly | Command output |
| A3 | Start the nested prototype from a Wayland host | A windowed nested KWin output opens without replacing or covering the host compositor | KWin log and screenshot |
| A4 | Close DaisyOS in the nested prototype | Only the nested KWin session closes; the host session remains usable | Tester note |
| A5 | Run the strict session preflight inside nested KWin | Wayland, KWin, XWayland, shell, portal, and PipeWire results are recorded | `session-report.md` |

## Gate B: shell surfaces and owned windows

| ID | Test | Expected result | Evidence |
|---|---|---|---|
| B1 | Open Launcher, calendar, notifications, power, and gaming controls | Each overlay stays inside the shell, receives input, and closes with Escape or an outside click | Screenshot and tester note |
| B2 | Open Settings, Files, Updates, and Welcome | Each DaisyOS-owned window opens as a normal top-level window with working custom controls | Screenshot |
| B3 | Move, resize, maximize, minimize, and restore owned windows | KWin owns placement; visible controls match the resulting window state and remain reachable | Tester note |
| B4 | Use Alt+Tab with shell-owned windows | Selection advances in both directions and activation restores minimized owned windows | Tester note |
| B5 | Open a menu near every screen edge | The menu remains fully visible and does not steal focus after dismissal | Screenshots |

## Gate C: native Wayland and XWayland applications

The prototype package list includes `foot` as the native Wayland test client and `xterm` as the XWayland test client.

| ID | Test | Expected result | Evidence |
|---|---|---|---|
| C1 | Launch `foot` from the nested session | It opens as a native Wayland window with correct focus, resize borders, and KWin decoration | `WAYLAND_DISPLAY` note and screenshot |
| C2 | Launch `xterm` | XWayland starts automatically and the window can move, resize, minimize, and close | Process note and screenshot |
| C3 | Switch among DaisyOS, `foot`, and `xterm` | KWin switching remains authoritative; no window becomes unreachable | Tester note |
| C4 | Copy plain test text between `foot`, `xterm`, and a DaisyOS text field | Clipboard transfer works in both directions without exposing stale content | Tester note; never attach clipboard data |
| C5 | Drag a disposable file between supported native/XWayland clients | The operation either succeeds or presents a clear unsupported state; the shell does not freeze | Tester note |
| C6 | Close or crash a test application | DaisyOS stays responsive and clears stale running state when observable | Shell log |

## Gate D: decorations, blur, and focus

| ID | Test | Expected result | Evidence |
|---|---|---|---|
| D1 | Compare a server-decorated app with a client-decorated app | KWin decorates supported windows; client-drawn controls remain owned by the application | Side-by-side screenshot |
| D2 | Resize from every window edge and corner | Pointer regions match visible borders with no dead or misleading resize area | Tester note |
| D3 | Enable and disable the reviewed KWin blur effect | Supported shell material changes gracefully; unsupported blur falls back to opaque/translucent material | Screenshots |
| D4 | Open overlays while another app has focus | Activation is deliberate, keyboard focus is not trapped, and dismissed overlays return focus correctly | Tester note |
| D5 | Run a fullscreen XWayland app, then exit it | The shell and normal desktop return without black frames, stuck focus, or hidden controls | Video or tester note |

## Gate E: displays and input

| ID | Test | Expected result | Evidence |
|---|---|---|---|
| E1 | Test 100%, 125%, 150%, and 200% scale | Text and controls remain sharp, correctly sized, and reachable | Screenshot set |
| E2 | Add and remove a virtual output | Windows and recovery UI return to an available output | KWin log and tester note |
| E3 | Mix two scale factors and refresh rates | Pointer mapping, popup placement, and window movement remain correct | Tester note |
| E4 | Exercise keyboard-only navigation | Launcher, Settings, dialogs, and recovery actions expose a visible focus path | Tester note |
| E5 | Suspend and resume the VM/session | Input, wallpaper, windows, portal, network, and audio recover without restarting the machine | Journal excerpt with personal data removed |

## Gate F: portals, audio, gaming, and creative workloads

| ID | Test | Expected result | Evidence |
|---|---|---|---|
| F1 | Open an Avalonia file picker | The KDE portal opens once, returns the chosen disposable file, and cancellation is harmless | Tester note |
| F2 | Request a screenshot or screen share through a portal-aware app | A permission prompt appears and capture state is visible to the user | Screenshot with private content removed |
| F3 | Play audio and switch the default output | PipeWire audio continues without shell failure or exposing backend errors | Tester note |
| F4 | Run a representative fullscreen game or Gamescope test | Frame pacing is stable, exiting restores focus, and critical notifications remain available | Frame-time capture and tester note |
| F5 | Run a color-sensitive creative app | DaisyOS does not force an unverified HDR or color conversion path | Tester note |

## Gate G: failure and recovery in QEMU

These rows must run in a disposable VM. The nested prototype intentionally exits with the shell and is not evidence for production restart behavior.

| ID | Test | Expected result | Evidence |
|---|---|---|---|
| G1 | Terminate the shell once | KWin and third-party apps survive; the session supervisor restarts only the shell | Journal excerpt |
| G2 | Trigger three consecutive shell failures | Restart attempts stop and the fallback marker/session appears without a loop | Journal and fallback screenshot |
| G3 | Start with KWin unavailable | The user sees “The desktop couldn’t start. Try the fallback session.” and can choose recovery | Screenshot |
| G4 | Restart the compositor in a disposable session | Failure is contained, logs are available, and recovery does not lose access to the fallback session | Journal excerpt |
| G5 | Boot the Xorg fallback | Core shell, Settings, Files, keyboard recovery, and shutdown remain usable | Preflight report and screenshot |

## Run record

Copy this table for each environment. Keep failures visible until the exact revision is retested.

| Field | Value |
|---|---|
| Date and revision | |
| Tester | |
| Host or VM image | |
| GPU and driver | |
| KWin version | |
| Display layout/scales | |
| Gates attempted | |
| Passed | |
| Failed | |
| Blocked | |
| Evidence location | |
| Release decision | Not ready / Ready for next gate |

### 2026-07-13 nested startup record

| Field | Value |
|---|---|
| Date and revision | 2026-07-13, `fd41309` plus working-tree validation changes |
| Tester | Codex automated preflight; interactive rows not claimed |
| Host or VM image | GNOME Wayland development host; nested KWin window |
| GPU and driver | NVIDIA Quadro RTX 4000; host-managed driver |
| KWin version | 6.7.2 |
| Display layout/scales | One fullscreen nested output at 100% |
| Gates attempted | A1, A2, A3, A5 |
| Passed | A1, A2, A3, A5 |
| Failed | None |
| Blocked | A4 and B1-F5 require interactive observation; G1-G5 require disposable QEMU |
| Evidence location | `artifacts/compositor/nested-kwin-session-report.md` (generated locally and ignored by Git) |
| Release decision | Ready for interactive nested gates; not ready for QEMU/release |

The published self-contained shell, KWin, and XWayland ran together on the
nested `wayland-1` socket. Strict preflight reported zero required failures and
zero warnings. Closing the test stopped only the nested process tree; this
command-line observation is not counted as the still-unobserved interactive A4
row.

### 2026-07-14 automated nested isolation record

| Field | Value |
|---|---|
| Date and revision | 2026-07-14, working tree after ISO lifecycle validation |
| Tester | Codex automated nested runtime harness; interactive rows not claimed |
| Host or VM image | GNOME Wayland development host; isolated nested KWin window |
| GPU and driver | Host-managed NVIDIA GPU; no hardware mutation |
| KWin version | 6.7.2 |
| Display layout/scales | One 1280x800 nested output at 100% |
| Gates attempted | A1, A2, A4, A5; A3 and C2 startup/isolation portions |
| Passed | A1, A2, A4, A5; nested compositor startup; native Wayland socket round trip; XWayland display and xterm startup |
| Failed | None |
| Blocked | C1 foot client is not installed; C2 move/resize/decorations and B1-F5 remain interactive; G1-G5 require disposable QEMU |
| Evidence location | `artifacts/compositor/nested-runtime/` (generated locally and ignored by Git) |
| Release decision | Ready for interactive nested gates; not ready for QEMU/release |

`scripts/test-kwin-nested-runtime.sh` verified the exact nested KWin PID, a
responsive unique Wayland socket, the running production shell, portals,
PipeWire, automatic XWayland startup on display `:3`, and a live xterm client.
It then ended only the nested process tree and confirmed the original host
Wayland connection still responded. The shell currently uses Avalonia's X11
backend through XWayland; `wayland-info` supplies the native Wayland client
round trip. No native-shell, focus, decoration, clipboard, or visual result is
inferred from this automated run. A3's visual containment evidence still comes
from the earlier observed nested-window run, not this headless assertion.
