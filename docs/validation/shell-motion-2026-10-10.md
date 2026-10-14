# Shell motion validation — 2026-10-10

Launcher, system flyouts, bottom bars and notification toasts use shared
220ms entrance and 140ms exit resources. Entrance decelerates into place;
exit accelerates away. Reversal samples the current transform and opacity
before cancellation and scales duration by remaining distance. Stale requests
cannot hide a reopened host. Launcher icon decoding no longer blocks appearance.
Taskbar reorder uses 160ms eased movement; press/lift transforms use 100ms
easing. Tray rotation uses 160ms easing. Shared durations honor Reduce Motion.

## Checks

- Shell compiled successfully during focused test builds.
- 24 focused checks passed: GuiMotionTests, NotificationPresentationTests,
  TaskbarPreviewTests, LauncherIconLoaderTests, AppTrayTests, QuickSettingsTests.
- The ordinary GUI test project currently fails compilation in unrelated
  OverallPolishTests.cs (removed PickerEntry.IsFileWithoutThumbnail) and
  NotesEditorTests.cs (obsolete three-argument PickerEntry constructor).
  Those files were excluded only through a temporary MSBuild target at
  /tmp/daisyos-motion-tests.targets; repository project settings were unchanged.
- Updated the tray test to sample the faster eased rotation earlier and the
  Quick Settings test to inspect the current BottomChromeOverlayHost viewport.
- git diff --check passed.
- Initial strict KWin session preflight: zero failures, zero warnings.

## Native acceptance

Files, Notes and Settings builds passed. The native development helper reloaded
the shell successfully (new shell PID 95106, original KWin PID 1017). The helper
used its bounded SIGTERM fallback before replacing the shell. Strict compositor
preflight after reload reported zero failures and warnings. Headless tests establish interruption,
position continuity, early hit-test release, opaque exits and resource timing.
They do not establish physical frame pacing or moving blur correctness.
Physical acceptance must exercise launcher, all four system flyouts, bar
autohide, toast dismissal, rapid reversal and Reduce Motion on the display.
