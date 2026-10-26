# Default Applications

DaisyOS ships with a set of built-in shell applications that provide the core desktop experience. These are registered in the `DaisyOS.Apps` project and discovered by the app launcher service.

## Built-in Apps

| App             | Description                                   | Icon                |
|----------------|-----------------------------------------------|----------------------|
| **Files**       | File manager with grid view, list view, and trash | `FilesIcon.png`       |
| **Settings**   | Shell settings hub                             | `SettingsIcon.png`   |
| **Terminal**   | Terminal emulator (system default)              | (system icon)        |
| **Mozilla Firefox**| Deafult pre installed browser               | apps own favicon       |
| **Launcher**   | App launcher (activates on click)              | `Launcher.png` |

## App Launcher

The launcher (Super / Win key) displays all available apps including:

- **Built-in shell apps** (Files, Settings, Terminal, Browser)
- **Real .desktop entries** discovered from XDG data directories (`/usr/share/applications/`, `~/.local/share/applications/`)
- **Pinned dock apps** and **recent apps**

Apps are grouped by category (e.g. "Development", "Games", "System") when the `.desktop` file provides a `Categories` field.

## Running App Indicators

Running applications show a small indicator dot below their dock icon. This works for both built-in and real desktop files.

## .desktop File Discovery

The `MockAppLauncherService` (used in both mock and real modes) discovers `.desktop` files from:

- `/usr/share/applications/`
- `/usr/local/share/applications/`
- `~/.local/share/applications/`
- `$XDG_DATA_DIRS` / `$XDG_DATA_HOME`

## Future Work

- Default app selection (choose which browser, terminal, file manager to use).
- `.desktop` file editing from within Settings.
- App ratings, auto-update, and Flatpak/AppImage integration.
