# Taskbar settings and four-edge docking — 2026-11-14

A dedicated Taskbar page uses desktop.svg in the Personalization sidebar and
search. Taskbar size and auto-hide moved from Appearance. It exposes docking
at Bottom/Left/Top/Right, app-dock alignment, three icon sizes, auto-hide and its
delay, media/system-bar/launcher visibility, date display, order locking,
running indicators, window previews, tooltips and reset. Reset preserves pinned
applications and their order. English and Finnish resources are included.

## Behaviour

- The app dock, system bar and media controls follow the chosen edge. Vertical
  docks use upright icons, scroll when crowded and keep icons readable.
- The vertical system bar stacks the regular network/audio/battery/Bluetooth
  presentation in one centred column, with date above time when enabled. A
  wider rounded pill keeps labels intact; the tray and notification buttons
  share its centre line. Its flyouts animate into the workspace and its tray
  arrow points inward. Full regional date/time is available on hover.
- One toggle enables auto-hide for the components. Reveal is independent:
  only the component under the pointer appears. Exact component slots, retained
  while hidden, define the reveal areas; gaps do not reveal any component.
  Independent hide timers and menu/preview holds remain in place.
- Layer-shell geometry updates anchor and dimensions in one commit, preserving
  the surface role, output and focus policy. Work-area reservation, desktop
  safe area, reveal sensors, flyouts, context menus, tooltips and preview
  coordinates follow the edge. Explicit input regions leave gaps and shadow
  bleed click-through. Reveal sensors cover only the matching component slots.
- Existing appearance/layout preferences are preserved; default docking stays
  at Bottom. New preferences normalize invalid values and persist through the
  shell-owned desktop-settings IPC.

## Verification

- 65 focused headless tests passed: four-edge layout and bounds, independent
  reveal and inactive gaps, settings in light/dark/narrow layouts, actual
  component flags, scrolling, menus opening inward, persistence, sizing,
  existing taskbar behaviour, preview holds and system-bar themes.
- The final column/centering layout rerun passed all 21 dock checks.
- 12 existing layer-surface contract tests passed.
- 14 QML preview-geometry tests passed, including crowded side-edge previews.
- The native acceptance probe moves one transparent, click-through,
  non-reserving layer surface through Bottom/Left/Top/Right/Bottom. This tests
  native geometry and input-region updates without moving the user's dock,
  changing preferences or rearranging desktop items. At 125% scaling, 82 logical
  units round to an 82.4-unit buffer, and the wider 114-unit side buffer
  rounds to 114.4; checks allow one physical pixel of rounding.
- `git diff --check` passed for the shell and the pinned Avalonia backend.

Local logs, screenshots, geometry results and preference backups are retained in
`artifacts/taskbar-settings-2026-11-14/` (ignored artifacts).

## Applied session

The Release shell and standalone apps were rebuilt. Shell PID 3981 was replaced
by PID 27570; KWin stayed PID 3254 and application windows remained running.
The current position, alignment, auto-hide choice and enabled date preference
were retained. Settings was already open and was left running; reopening it
loads the updated page labels. No recent fatal/protocol errors were found.
The final native probe passed with 1536×82.4 horizontal and 114.4×864 vertical
buffers at the current 125% scale.

## Vertical system bar refinement

Vertical docks now display the compact regional numeric date without a weekday
and hide the battery glyph while retaining the percentage and status tooltip.
The shared battery control has a `ShowIcon` property defaulting to true, so
horizontal docks and other battery presentations retain their icons. Returning
to a horizontal edge restores the system bar glyph. Full date/time remains in
the calendar button tooltip. All 21 dock placement checks passed; left/right
render fixtures now show a numeric date and percentage-only battery.

The subsequent shape correction removes the side-only 84px width and 42px
pill radius. Side system bars use 52px width, 40px controls and the same
RoundedXL/Inset/DoubleInset resources as horizontal bars. Surface reservation
also follows the narrower bar. All 21 placement checks pass, including side
width and shared-radius assertions; rendered fixtures confirm numeric date
and percentage fit the narrower column.
