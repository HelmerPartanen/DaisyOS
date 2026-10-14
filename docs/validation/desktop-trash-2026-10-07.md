# Desktop Trash — 2026-10-07

The desktop icon chooses existing themed Trash_Light/Trash_Dark assets when the trash files directory is empty and Trash_Item variants when files or directories exist. State is refreshed immediately after desktop moves and once per second while the desktop is loaded, including changes from other file managers. The refresh timer stops on unload.

Trash occupies a reserved layout cell. Normal-item insertion chains skip it; widget reflow reserves it; passive FLIP animation excludes it. It remains a movable drag source and can reorder normal items. Hovering a real desktop item over its cell suppresses reorder, and pointer release invokes the shared Files trash operation. External file-manager drops report Move and use the same operation. Pointer release completes before capture is relinquished, so capture-loss cancellation cannot discard the pending drop.

Desktop moves now write restorable .trashinfo metadata through FileSystemService and remove the desktop entry only on success. Metadata is reserved before moving data, including collision checks against both contents and metadata; failure removes only the newly reserved metadata. Deletion timestamps use invariant formatting across system languages. This is a recycle-bin operation; no user's existing Trash contents are emptied by automated checks.

Tests cover empty/full icon state for files and directories, fixed Trash behavior across reorder chains, dragging Trash to displace another item, and a real owned-file pointer drag/drop with restore metadata. The end-to-end fixture is restored and removed individually. Physical Wayland pointer acceptance and mounted-volume trash paths remain outside these automated checks. The current shared trash implementation uses the home trash directory; cross-filesystem moves can fail safely.

Git HEAD remains corrupt; task-start desktop backups are at /tmp/daisy-desktop-trash-before. The previously documented complete-solution designer prerequisite remains unchanged.

Final focused desktop result: 33 passed, zero failed. The integration fixture verifies a pointer drop under fi-FI culture and restores the individually owned test file.
Native builds and shell reload completed successfully. Strict compositor preflight returned zero failures and zero warnings. No user applications were closed and no existing trash contents were emptied.
