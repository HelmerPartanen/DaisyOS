# Standalone applications

Files, Notes, Settings and Disk Manager run outside the desktop process.

- Project: `src/DaisyOS.Files/DaisyOS.Files.csproj`.
- Executable: `DaisyOS.Files`, installed launcher `daisyos-files`.
- Desktop identity: `org.daisyos.files`.
- Build: `dotnet build src/DaisyOS.Files/DaisyOS.Files.csproj -c Release`.
- Development installation: `scripts/install-system-apps.sh` (all four apps).

The desktop launches Files through its desktop entry or the packaged launcher.
Files has its own Avalonia dispatcher, renderer, main-window lifetime and file
operation services. Closing Files exits that process; restarting the desktop
does not close Files. Repeated launches forward paths and `--select` to the
existing instance through a user-only named pipe. The ownership mutex is global
because terminal and desktop launches can have different Linux process sessions.

Files owns `org.freedesktop.FileManager1` while running. The installed D-Bus
service starts Files on demand for ShowFolders/ShowItems requests. Installation
preserves an existing third-party activation service rather than displacing it.
LocalSend and desktop feedback remain shell-owned and are requested through the
existing shell D-Bus endpoint. File operations, browsing and clipboard actions
execute in Files. Appearance preferences are read from the shared settings file
and applied locally; the application does not rewrite desktop appearance.

This is a process and packaging migration, not a new UI implementation. The
project still references DaisyOS.Shell to reuse existing Files views, controls,
compiled resources and application appearance support. A subsequent pass can
extract those resources into a shared UI library without duplicating them.
Published Files lives under `apps/Files` with its own runtime payload so its
ReadyToRun assemblies cannot overwrite the desktop payload.
Notes and Settings have separate executable projects at `src/DaisyOS.Notes`
and `src/DaisyOS.Settings`, desktop identities `org.daisyos.notes` and
`org.daisyos.settings`, and launchers `daisyos-notes` and `daisyos-settings`.
They use the same normal Wayland toplevel creation as Files. Repeated launches
activate the existing app. Notes accepts `--new-note`; Settings accepts
`--category=Appearance` (or another existing category). Notes retains autosave,
save-on-close and the unsaved-changes confirmation, with storage in the existing
Documents/Notes location.

Disk Manager uses `src/DaisyOS.DiskManager`, identity `org.daisyos.diskmanager`,
and launcher `daisyos-diskmanager`. It shares the same single-instance host,
appearance observer, shutdown behavior and compositor activation as Notes and
Settings. The taskbar gives every running native app a temporary slot when it
is unpinned, removes that slot after closing, and preserves pinned entries.
Disk Manager's light/dark artwork uses the shared `AppIconDiskManager` resource.

Settings runs hardware operations through the existing Linux services. Desktop
appearance, wallpaper, dock and shell-preference changes go through a user-only
request/reply pipe to the desktop, which remains the sole owner of those writes.
The reply follows completion of the real operation. Requests are serialized and
failures are reported rather than saved as successful local preferences. Apps
refresh read-only snapshots and appearance while running. Settings can still
inspect the system when the desktop is unavailable, but desktop-owned changes
require the DaisyOS desktop to be running.

All four packages currently reuse views/resources from DaisyOS.Shell. Moving
those into a shared UI library remains a separate refactoring; process isolation
does not require duplicating their UI. Each published app has its own runtime
under `apps/Files`, `apps/Notes`, `apps/Settings` or `apps/DiskManager`.

Native Development keeps the shell in Debug and runs Files, Notes, Settings and Disk Manager
in Release by default, matching the startup benchmark. Its restart command builds
both configurations as needed and refreshes the installed launchers. To explicitly
install Debug apps, use `DAISYOS_APP_CONFIGURATION=Debug scripts/install-system-apps.sh`
after building those apps. Set the same environment variable for the native session
when debugging standalone launches. Managed desktop copies with known DaisyOS
binary paths are refreshed; customized commands and shortcut metadata are preserved.
