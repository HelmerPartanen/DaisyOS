# Desktop settings menu and Delete — 2026-11-14

Desktop settings is consistently available in the empty-desktop context menu,
item context menus and native widget context menus. Each action dismisses its
menu and opens the Desktop Settings category.

Delete on the desktop removes the visible selected items: files and folders use
the existing recoverable Trash operation and gap-filling layout path; widgets
use the existing removal path. Trash itself remains protected. Selection is
snapshotted for asynchronous deletion, repeat-key operations cannot overlap,
and layout is saved afterward. Rename editors, text inputs and interactive
controls retain their own Delete behavior. Modified Delete shortcuts are not
interpreted as permanent deletion.

Selecting an item focuses the desktop interaction surface. Native desktop content
now uses on-demand Wayland keyboard interaction; wallpaper remains passive.
The Release shell was reloaded without restarting KWin or other applications.

Validation: 9 desktop checks and 12 layer-surface policy checks passed. Trash tests
use isolated XDG data/layout paths and only temporary test files/folders, verifying
nested contents and trash metadata. Tests also cover widget deletion, rename
protection, protected Trash, and the actual opened context menus. Logs are under
artifacts/desktop-delete-2026-11-14/. git diff --check passed.
