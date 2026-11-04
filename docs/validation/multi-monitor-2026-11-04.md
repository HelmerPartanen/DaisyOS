# Multi-monitor validation — 2026-11-04

## Defects and fixes

| Observed defect | Implemented behavior |
| --- | --- |
| Display settings append repeated sections when selecting another output | Rebuild one page, cancel obsolete loads, reject stale results, and retain selection by connector name. |
| Secondary output has no wallpaper | Maintain one native wallpaper surface per active output, synchronize wallpaper changes, and reconcile surfaces after hotplug. |
| No mirror/extend controls | Apply an atomic KScreen configuration with the existing Keep/Revert confirmation. Returning to Extend restores the arrangement remembered during this Settings session. |
| No output positioning controls | Provide a draggable logical-coordinate arrangement and numeric horizontal/vertical positions. Reject overlapping drafts and invalidate drafts when the connector set changes. |
| Secondary fullscreen video hides primary bars | Report coverage per output and scope primary shell visibility to its output, including fullscreen clients omitted from the taskbar. |
| Scale or selected output changes unexpectedly | Display each output's actual scale, preserve precise refresh rates, normalize primary priorities, and restore only connectors still present during rollback. |

Primary-output changes also migrate existing shell surfaces and preserve the output choice for surfaces first shown later or hidden and reopened. Wallpaper occlusion is scoped per output. Lock-screen output reconciliation handles topology changes; application window sizing prefers the window's current screen.

## Native acceptance

`artifacts/multi-monitor-final-2026-11-04/results.json` records a successful isolated KWin run with two virtual outputs. All nine groups passed:

1. Two wallpaper surfaces with presented content and synchronized changes.
2. Primary-output migration preserves native shell surfaces.
3. Unmapped overlays retain their output across hide/show.
4. Mixed per-output scaling.
5. Repeated output selection renders exactly one settings page.
6. Mirror/extend Keep and full-layout Revert.
7. Horizontal and vertical display arrangement.
8. A real fullscreen client on the secondary leaves primary bars available; moving it to the primary hides them.
9. Disable/re-enable reconciles wallpaper surfaces.

The harness uses real layer-shell surfaces, KScreen commands, a PySide6 fullscreen client, and KWin scripting. Its output migration uses the [documented KWin workspace API](https://develop.kde.org/docs/plasma/kwin/api/). The harness refuses to run outside its explicitly named isolated Wayland session.

Reproduce after building the Release shell:

```sh
dotnet build src/DaisyOS.Shell/DaisyOS.Shell.csproj -c Release -m:2
scripts/run-multi-monitor-acceptance.sh artifacts/multi-monitor-local
```

Requires KWin Wayland with the virtual backend, kscreen-doctor, DBus session tools, and Python with PySide6. The wrapper isolates configuration/state/cache and sets Qt to Wayland with the KDE desktop environment to avoid inherited portal/platform-theme behavior.

## Regression checks

The focused core run passed 62 tests covering configuration parsing, logical geometry, restoration after unplug, output coverage, and existing desktop/window contracts. The focused headless UI run passed 17 tests covering repeated selection, canceled/stale loads, output reordering, arrangement drafts and overlap rejection, display confirmation, lock-screen media, and wallpaper transitions. KWin bridge, overview, and taskbar-preview package checks pass. Both staged and working-tree whitespace checks pass.

Visual review of the arrangement editor caught clipped negative coordinates; the numeric fields were widened to fit the allowed range.

The localization catalog includes the new controls. Full catalog verification remains blocked by the pre-existing literal `Widget Gallery` in `WidgetLibraryOverlay.axaml`.

## Boundaries

Virtual KWin acceptance verifies real compositor behavior but does not certify physical displays, GPU drivers, HDR, suspend/resume, high refresh rates, or authenticated lock-screen hotplug. These require a physical session acceptance pass. The physical HDMI-A-1/eDP-1 arrangement was left unchanged, and the running desktop was not restarted. Apply the updated shell and packaged KWin bridge together before physical acceptance.

No claim of frame-time or latency improvement follows from these functional checks. Video-wallpaper recovery across physical output migration was not separately certified.
