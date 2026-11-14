# Settings context-menu shortcuts — 2026-11-14

Desktop Change wallpaper opens Appearance and focuses the wallpaper picker.
Desktop settings opens Desktop. The Desktop page wallpaper shortcut uses the
same destination. SettingsDestination carries category, section and setting
through both command-line and existing-instance activation. Navigation clears
search and previous scroll offsets before focusing the requested control.

Right-clicking taskbar empty space opens Taskbar settings, Position and size,
Auto-hide and Desktop settings. App and launcher buttons retain their own
behavior. Selection dismisses the menu before requesting Settings. Layout and
Auto-hide shortcuts focus their own section. New labels have English/Finnish
resources. The cartouche points at the clicked position and accounts for taskbar
layout scaling while excluding temporary reveal translations.

58 Release checks passed: argument round-trips, navigation in an existing Settings
window, wallpaper picker visibility/focus, auto-hide focus, real bubbled right
clicks, dismissal and destinations at all four edges and normal/reduced scales,
plus dock/cartouche regressions. Settings Release build and diff checks passed.

The Release shell was reloaded, preserving KWin and other applications. Existing
Settings process 19457 kept its old activation handler after a normal close
request; it was not force-terminated. Close/reopen Settings once to load updated
section navigation. Logs: artifacts/settings-context-shortcuts-2026-11-14/.
