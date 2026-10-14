# Launcher first-frame correction — 2026-10-11

## Change

The hidden launcher's first opening calculated slide distance before layout.
The regression test reproduced missing launcher bounds at animation start.
That could select the 82px shell fallback instead of the full 620px card height.

Launcher opening now prepares layout while the host is transparent, prepares
the two static decorative frame images, positions the card below the viewport
clip edge, then exposes the host. The card stays opaque throughout its slide.
The dedicated entrance is 280ms with spline (0.2, 0, 0.2, 1), starting and ending
gently. Dismissal remains 140ms. Reversal skips hidden preparation and retains
current position and opacity. Catalog icon decoding remains asynchronous.

## Verification

- The new first-open regression failed against the previous implementation with
  "First-open motion must use laid-out launcher geometry."
- 10 focused checks passed: GuiMotionTests, LauncherIconLoaderTests,
  NotificationPresentationTests and ShellFrameRenderingTests.
- The first-open check uses an already mapped clipped overlay and an empty
  in-memory catalog. It verifies full layout before motion, complete initial
  placement below the actual clip edge, upward intermediate displacement,
  opaque content, uninterrupted position on reversal and zero-duration reopen.
- Headless samples pump one UI render pass per timer tick to advance the
  animation clock without draining the full animation.
- Toast tests now override the current shell timing resources.
- Two unrelated stale PickerEntry test files remain excluded via the temporary
  /tmp/daisyos-motion-tests.targets build setting, as documented in
  shell-motion-2026-10-10.md. Repository project settings were not changed.
- git diff --check passed.

## Native session

Files, Notes and Settings rebuilt successfully. Native shell reload completed
with shell PID 107276; KWin retained PID 1017. The helper used its bounded
SIGTERM fallback. Strict session preflight reported zero failures and warnings. Headless results do not establish physical
frame pacing or moving blur correctness. The user's display feedback remains
the acceptance criterion for the resulting feel.
