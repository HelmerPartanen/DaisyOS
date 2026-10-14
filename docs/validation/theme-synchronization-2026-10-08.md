# Theme synchronization — 2026-10-08

Context-menu SVG icons now observe mutable foreground color and opacity, resolve visual ancestors and the actual theme variant, and release brush subscriptions when detached. The menu icon presenter forwards its foreground explicitly. Intermediate SVG colors are not cached indefinitely.

Files sidebar and tab icons previously resolved semantic resource keys through a one-time converter. They now bind to the resource observable, following theme and accent changes while their window stays open. Standalone apps observe atomic preference-file replacements with a 40ms debounce; their existing one-second poll remains available for automatic scheduling and watcher failure.

The application-wide transition policy covers derived controls, adds missing supported brush/color transitions, and preserves explicit interaction transitions. It uses the shared MotionDurationBase resource. SVG CurrentColor follows the foreground directly. Image sources cross-fade at animation priority, preserving their original bindings and retargeting rapid changes. Reduced motion removes policy-owned color transitions and makes image changes immediate. External applications and compositor-owned decorations are outside this Avalonia policy.

Six focused headless tests pass: mutable icon paints, repeated menu reopening in both themes with rendered pixel checks, resource-key theme/accent updates, intermediate/final color frames with interaction preservation and reduced motion, image cross-fade retargeting with binding preservation, and atomic preference replacement with observer disposal.

The affected theme, tray, Quick Settings, app chrome and grouped-taskbar checks pass 22/22. The final complete GUI run passed 139/140; its sole failure was the previously documented LauncherIconLoader refresh timeout, which passed its isolated rerun. The earlier reports in `control-polish-2026-10-07.md` and `language-support-2026-10-07.md` record the same full-suite timeout. Taskbar back edges follow the front tile's animated paint directly to avoid an extra animation delay. Theme endpoint checks now wait for the transition rather than asserting an immediate color switch.

The core suite passes 382 tests with one skipped live KWin screenshot test. A stale Files source-contract assertion was updated to include the Large sidebar variant added in the previous button-size task. `git diff --check` passes; unrelated theme-frame highlight edits were preserved.

The framework implementation and design follow [Avalonia control transitions](https://docs.avaloniaui.net/docs/graphics-animation/control-transitions) and [theme resources](https://docs.avaloniaui.net/docs/app-development/resources). The SVG renderer's CurrentColor reload/cache behavior was checked against its [pinned source](https://github.com/wieslawsoltes/Svg.Skia/blob/3261e036769d0deba1f621265c7ab7000e3ce470/src/Svg.Controls.Skia.Avalonia/Svg.cs).

Physical Wayland theme-switch appearance remains a user acceptance check. Apps already running an older executable must be reopened once to load these changes.

Files, Notes and Settings native builds succeeded with zero warnings/errors. The controlled shell reload succeeded (shell PID changed from 1439 to 18276; KWin stayed at 1042), preserving open application processes. Strict compositor preflight returned zero failures/warnings, and the new shell startup log contains no theme-policy exceptions.
