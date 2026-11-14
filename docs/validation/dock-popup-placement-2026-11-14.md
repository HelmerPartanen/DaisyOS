# Dock popup placement — 2026-11-14

System flyouts now anchor to the actual opening button instead of sharing an
output-corner alignment. The positioning service converts the detached panel's
coordinates into the popup parent's coordinates, accounts for the overlay
viewport inset, centers along the dock axis, and clamps inside the available
output with a 6px clearance. Context menus use the same placement, with their
horizontal pointer aimed at the opening icon after clamping. Side menus now have left/right-facing pointers with a vertical anchor coordinate.
Content padding reserves space on the selected pointer edge, and the frame
geometry rotates independently of the upright menu content. Media menus follow
the same rules.

Opening measures the popup independently of its old position, preventing
repeated context-menu openings from drifting. Subsequent layout notifications
use the arranged size without invoking Measure again; remeasuring from this
callback caused a native render loop during initial validation and was removed.
Popups follow anchor/content/output layout changes and retain their original
maximum dimensions when the viewport grows. Available viewport dimensions cap
popup sizes. Edge changes dismiss existing system flyouts and media menus.

The native flyout viewport initializes from the selected edge and actual
reserved extent, including all taskbar sizes. Top flyouts enter downwards from
above the viewport; other edges enter inward from their own edge. Translation
covers the entire card to the clipping boundary, rather than a fixed 82px.

Validation:

- 61 dock placement, bottom-chrome and motion checks passed in Release.
- 12 additional rendered-frame checks passed: four edges, three taskbar sizes,
  and all four system flyouts with opening-button placement and directional
  animation assertions.
- Repeated detached context-menu openings and content-size changes pass on all
  four edges.
- Native session: opened and captured Background apps at each edge, closed it,
  restored the original taskbar preferences, and checked the session log for
  new layout loops or UI recovery failures. No errors occurred on the corrected
  build. Captures were visually inspected for side and horizontal placement.
- KWin PID 3254 and existing applications remained running. Corrected Release
  shell PID 60656 is active; user's original Left position is restored.
- Captures, logs and native-check script are retained under
  `artifacts/dock-popup-placement-2026-11-14/`. `git diff --check` passes.

## Four-direction cartouche follow-up

CartoucheBorder now supports Bottom, Top, Left and Right pointer edges, with
separate horizontal/vertical anchor coordinates. Shape caching includes the
selected edge; measure and arrange reserve the pointer on that edge. Existing
TailOnTop callers remain supported. Zero-height tails draw a plain rounded
frame. Corner clamping keeps the pointer inside the contour near output edges.

52 cartouche, dock placement and bottom-chrome checks passed in Release. New
checks cover all four contours, screen-edge clamping, content padding, upright
text and rendered frames. Left/right rendered captures were visually inspected.
Artifacts: `artifacts/cartouche-directions-2026-11-14/`. Updated Release shell
was reloaded while preserving KWin and applications.

## Flyout endpoint snap correction

The opening path previously measured a desired size and started animation before
arranging the popup. A new assertion checking arranged bounds immediately after
opening failed for all 12 edge/size combinations on the previous build. Position
now commits the opening button's root layout, positions the popup, and commits
the popup root's layout before returning to the animation caller. Any correction
for wrapping, layout rounding or arranged dimensions completes before motion.
LayoutUpdated callbacks still avoid explicit Measure or UpdateLayout calls.

Anchor coordinates now sum resting layout bounds instead of using TranslatePoint,
which includes temporary dock reveal transforms. This keeps popup resting bounds
stable when an auto-hide reveal transform is applied or removed. Actual anchor
layout changes, content changes and output changes continue to trigger reflow.

69 Release checks passed, including immediate final bounds, stable rendered
popup frames and temporary dock translations in all four directions. The existing
GUI motion tests cover crisp final transform cleanup and interrupted motion.
Native flyouts were reopened on all four edges without new layout-loop or UI
recovery errors. Original user preferences were restored (Bottom at this check).
KWin PID 3122 remained running; updated Release shell PID 14943 is active.
Before/after test logs and native validation are retained under
`artifacts/flyout-snap-2026-11-14/`.
