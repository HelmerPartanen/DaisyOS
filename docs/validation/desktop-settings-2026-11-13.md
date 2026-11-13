# Desktop settings and stable rearrangement — 2026-11-13

Settings now has a Desktop page in Personalization and in search. The desktop
context-menu entry opens it directly. Its controls are backed by the existing
shell-owned preference IPC and persisted session state:

- Auto-arrange new items (off by default).
- Fill from the left, right, top or bottom edge.
- Fill gaps after removal (on by default, independently configurable).
- Lock dragging and widget resizing.
- Show/hide desktop items, Trash and widgets without deleting them.
- Icon sizes of 32, 48 and 64 pixels.
- Single-click or double-click activation; dragging does not launch an item.
- Arrange now, sort by name/type/size/modification date, and a wallpaper shortcut.

Widget movement previously repacked every normal icon from the first grid cell.
It now reserves unaffected icons before relocating only overlapping icons. A
same-cell drag is a no-op, including when the widget centre is over another
widget. Layout uses the real widget spans, preserves saved manual positions on
startup, and saves changed layout cells. Deleting files through the desktop
watcher, moving files to Trash, or removing a widget compacts icons in their
current visual order when gap filling is enabled. Remaining widgets stay put.

FLIP retargeting now includes the currently rendered translation/scale rather
than jumping to the previous target. Idle arrange passes do not restart motion,
and the global Reduce motion preference suppresses FLIP motion.

Appearance now has one auto-hide switch for all three bottom components. A
single IPC operation changes and saves the group atomically. Legacy mixed
settings migrate to on if any component previously used auto-hide; the explicit
new group value takes precedence afterward. The Battery sidebar item now uses
the same monochrome DaisyIcon/ContentPrimaryBrush contract as its neighbours.
New UI strings include English and Finnish resources.

## Verification

- 34 desktop view-model tests passed, including no-op widget motion, preserving
  distant icons, collision-only displacement, gap filling on/off, locking,
  visibility, sizing and restoring manual positions when loading preferences.
- 7 core grid tests passed, including all four alignment traversal orders.
- 13 focused headless tests passed: FLIP continuity and settling, desktop page
  layout in both themes at 760/1000px, preferences fallback, matching sidebar
  icon type, one bottom visibility toggle and legacy auto-hide migration.
- 11 existing Settings layout checks also passed in the earlier broader run.
- Two existing DesktopRenameLayoutTests cases fail because they reject a Files
  app icon for a folder. DesktopItemLoader.ResolveFileIconPath already returns
  GetAppIconPath("files", isLight) for directories in HEAD; this loader was not
  changed by this task. Other checks in that run passed. No failing assertions
  were weakened to hide the mismatch.
- `git diff --check` passed.

Logs, desktop screenshots and copies of the pre-reload appearance/layout files
are under `artifacts/desktop-settings-2026-11-13/` (ignored local artifacts).

## Running session

The Release shell was rebuilt and reloaded (PID 56893 → 73581), keeping KWin
PID 3148 and open applications running. Standalone application outputs were
rebuilt too. A live bottom-auto-hide IPC request returned ok and persisted the
same logical value across the new group preference and all three compatibility
flags. The permissions/portal backend was reloaded and is active. Settings was
not already open, so the updated Release Settings application was opened directly
to Desktop; its window was confirmed mapped by the compositor bridge.
