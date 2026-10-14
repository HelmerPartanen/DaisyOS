# Compact control polish — 2026-10-07

## Changes

Desktop action buttons, single-line text fields, dropdowns, and application search fields use a shared 32px height. Compact and Comfortable remain compatible aliases. Local desktop overrides in Settings, Notes, Quick Settings, media widgets, app dialogs, title-bar actions, and the widget library use the shared resources. Multiline documents, calendar cells, taskbar app slots, and content tiles retain their structural geometry.

The shared confirmation dialog uses a 16px semibold heading, secondary explanation, quiet Cancel default, and a rightmost destructive action. It has no minimize/maximize controls or independent taskbar entry. Its existing cancel/confirm results remain unchanged.

The Settings wallpaper preview has no border. Button pressure scales the whole button around its center, with no separate content transform. The Notes floating toolbar uses a container spacing token so its 32px controls fit inside its padded surface.

## Automated and visual evidence

- Core suite: **376 passed, one skipped**. The three widget library icon-size token violations are corrected.
- Five new headless rendered checks cover matching button, picker, text, and search heights in light/dark themes; safe default confirmation actions; and a held pointer press preserving the content center without a second transform.
- Final focused control, widget layout, usability, and launcher-icon checks: **27 passed**. Final complete GUI suite: **119 passed, one failed**. Its sole failure is `LauncherIconLoaderTests.CatalogIconsLoadWithoutRealizedTilesAndRefreshWhenChanged`: `WhenReady` times out during the second icon refresh in the full suite, while the isolated/focused run passes. No launcher loader code was modified.
- The new pressed-animation check initially sampled the transition before it settled; it now explicitly ticks rendering and waits for the shared animation to finish. All five new polish checks pass in the final complete GUI run.
- Light/dark confirmation and control screenshots were visually inspected. Artifacts are under `artifacts/control-polish-2026-10-07/` (ignored by Git).
- The full solution gate fails because the bundled Avalonia designer invokes missing `dnx Bun.Unofficial.Tool --yes -- install` (exit 127).
- Repository-wide formatting reports extensive existing whitespace/import violations in shell and bundled Avalonia files. The failed check was stopped after capturing those diagnostics; unrelated files were preserved.
- Native Files, Notes, and Settings builds: **zero errors**. Native shell reload succeeded (shell PID 36405), with KWin PID 1024 and existing apps preserved.
- Live strict compositor preflight after reload: **zero failures, zero warnings**. The native tray screenshot was inspected: compact controls and Steam tray icon render correctly; the popup was closed afterward.
- The System Bar quick-settings button interior now uses the same 32px token as its hit target. This last container alignment change was confirmed by the rebuilt native runtime.

Git HEAD's loose object remains corrupt. Before editing, touched files were copied into `/tmp/daisy-controls-before/`; a standalone task diff is saved with the ignored artifacts. This permits review without rewriting existing work or attempting Git recovery.

## Runtime limits

Headless screenshots validate Avalonia content, not KWin's native title-bar rendering. Existing standalone Settings and Notes processes must be reopened to load rebuilt code; a shell restart replaces shell surfaces while preserving KWin and open apps. User acceptance of the revised controls remains separate from automated validation.
