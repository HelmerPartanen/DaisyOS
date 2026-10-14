# DaisyOS keyboard shortcuts

This document is the authoritative list of keyboard shortcuts currently implemented by DaisyOS. The system bindings follow the Windows 11 conventions in `docs/keyboard-shortcuts.md` where DaisyOS has the corresponding feature.

`Super` refers to the Windows or Meta key.

## System and shell

| Shortcut | Action |
|---|---|
| `Super+Tab` | Open or close Task Overview |
| `Super` | Open or close Launcher when the key is released by itself |
| `Ctrl+Esc` | Open or close Launcher |
| `Super+S` | Open or close Quick Search |
| `Super+I` | Open Settings |
| `Super+E` | Open Files |
| `Super+N` | Open or close Notification Center |
| `Super+V` | Open Clipboard History |
| `Super+Shift+N` | Create a note and focus its editor |
| `Super+L` | Lock DaisyOS |
| `Print Screen` | Open DaisyOS Screenshot with a fresh screen capture |
| `Ctrl+Shift+Escape` | Open Task Manager |
| `Alt+Tab` | Cycle through DaisyOS-owned application windows |
| `Shift+Alt+Tab` | Cycle backward through DaisyOS-owned application windows |
| `Alt+F4` | Open the power menu when the desktop shell is active |
| `Ctrl+Alt+Delete` | Open or close the power menu |
| `Escape` | Close the active shell overlay |

These are shell-level bindings. Window switching and window-management shortcuts for third-party applications remain compositor-owned.

## Quick Search

| Shortcut | Action |
|---|---|
| `Down` | Select the next result |
| `Up` | Select the previous result |
| `Enter` | Open or execute the selected result |
| `Escape` | Close Quick Search |

## Launcher search

Launcher filters applications as you type. Pressing Enter resolves the current query immediately, including when you type faster than the search debounce.

## Files

| Shortcut | Action |
|---|---|
| `Ctrl+C` | Copy the selected item |
| `Ctrl+X` | Cut the selected item |
| `Ctrl+V` | Paste into the current folder |
| `Delete` | Move the selected item to Trash |
| `Shift+Delete` | Request permanent deletion |
| `F2` | Rename the selected item |
| `Enter` | Open the selected item |
| `F5` or `Ctrl+R` | Refresh the current folder |
| `Ctrl+Shift+N` | Create a folder |
| `Ctrl+L` | Edit the current path |
| `Ctrl+T` | Open a new tab |
| `Ctrl+W` | Close the active tab, or the window if it is the last tab |
| `Ctrl+Tab` / `Ctrl+Shift+Tab` | Select the next / previous tab |
| `Ctrl+F` or `F3` | Open and focus folder search |
| `Escape` | Clear folder search; close its popup when open |
| `Ctrl+H` | Show or hide hidden files |
| `Ctrl+Z` | Undo the last move to Trash when focus is outside a text editor |
| `Alt+Left` | Go back |
| `Alt+Right` | Go forward |
| `Alt+Up` | Open the parent folder |

## File picker

| Shortcut | Action |
|---|---|
| `Ctrl+F` | Focus folder search |
| `Enter` on a folder | Open that folder |
| `Ctrl+Enter` | Confirm the selected files |
| `Alt+Left` / `Alt+Up` | Go back / open the parent folder |
| `Escape` | Clear search first; cancel the picker when search is empty |

## Notes

| Shortcut | Action |
|---|---|
| `Ctrl+N` | Create a note and focus the editor |
| `Ctrl+S` | Save the current note |
| `Ctrl+F` | Focus note search |
| `Escape` in search | Clear note search |

Notes also saves edits automatically. If saving fails, the error remains visible with a retry action; closing the window asks before discarding unsaved text.

`Super+Shift+N` provides the direct desktop path: it opens or restores Notes, creates one note, and focuses the editor.

## Settings and App Store

`Ctrl+F` focuses search. In Settings search, `Up` and `Down` move through results, `Enter` opens a result, and `Escape` clears search and restores search focus. In App Store, `Escape` clears the current query.

## Desktop

| Shortcut | Action |
|---|---|
| `Ctrl+C` | Copy the selected desktop item |
| `Ctrl+V` | Paste onto the desktop |
| `Delete` | Move selected desktop items to Trash |
| `F2` | Rename the selected item |
| `Enter` | Open the selected item |
| `F5` or `Ctrl+R` | Refresh desktop items |
| `Ctrl+Shift+N` | Create a folder on the desktop |

## Calendar and editing controls

The calendar supports arrow-key day navigation, `Page Up`, `Page Down`, and `Home`. Standard Avalonia text controls provide conventional editing bindings such as `Ctrl+C`, `Ctrl+X`, `Ctrl+V`, `Ctrl+A`, `Ctrl+Z`, word navigation, and selection.

## Development-only shortcuts

These bindings are disabled in production mode.

| Shortcut | Action |
|---|---|
| `Alt+R` | Open or close shell resource diagnostics |
| `Ctrl+Q` | Exit the development shell |

## Reserved or not yet implemented

The following Windows-style bindings are intentionally not claimed until DaisyOS has the corresponding complete feature: `Super+A` Quick Settings, `Super+R` Run, virtual-desktop shortcuts, voice typing, and accessibility-tool toggles.

Task Overview uses Windows Task View’s `Super+Tab` convention. macOS uses `Control+Up Arrow` for Mission Control. Launcher input is owned by the persistent KWin bridge; overview input is owned by its compositor effect.
