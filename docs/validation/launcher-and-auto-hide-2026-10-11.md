# Launcher timing and independent auto-hide chrome

Launcher now consumes `MotionDurationShellEnter` (220ms) and `MotionEaseEnter` directly, matching systembar panels. Its former 280ms duration and gentle-start easing were removed. Full-height opaque translation, first-frame layout preparation, interruption handling and reduced-motion behavior remain intact.

Taskbar and systembar reveal independently in their own bottom-edge regions and retain separate hide timers. A visible native panel captures input across the bottom strip, covering the separate edge sensor. Both surfaces now route their bottom 2px of pointer input to the same reveal handler. The native panel's transparent background provides a hit target in the empty areas. This allows the hidden bar to be revealed while the other is still visible, without automatically revealing both or keeping one visible because the other is hovered. Separate visibility preferences and fullscreen suppression remain supported.

Focused headless coverage includes independent reveals in both orders through the actual detached panel, ignored edge positions, fullscreen suppression and the existing launcher first-frame/reversal checks. Physical display feel remains subject to user acceptance.

## Verification

- Focused `GuiMotionTests`: 7 passed, 0 failed.
- The launcher test asserts the production 220ms entry token, then uses a longer test-only duration to sample intermediate geometry reliably under headless render load.
- As in the earlier motion checks, the temporary MSBuild target excludes the unrelated stale `OverallPolishTests` and `NotesEditorTests` files; the repository test project is unchanged. The full repository suite was not run.
- `git diff --check` passed.
- Native Files, Notes and Settings builds completed without errors; shell reloaded successfully while KWin and open applications remained running.
- Strict compositor session check passed with zero failures and zero warnings.

The corrected independent-reveal implementation passed all seven focused motion tests, including both hover orders on the detached native panel with a transparent background and no Window background. Native application builds and shell reload succeeded; strict compositor health reported zero failures and zero warnings. Physical pointer acceptance remains user verification.
