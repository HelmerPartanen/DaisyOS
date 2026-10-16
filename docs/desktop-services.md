# DaisyOS desktop services

DaisyOS supplies portal dialogs in a separate Avalonia process, launched with
`DaisyOS.Shell --desktop-services --authentication-agent`. The process owns
`org.freedesktop.impl.portal.desktop.daisyos`; the public
`org.freedesktop.portal.Desktop` remains the standard xdg-desktop-portal frontend.
KWin supplies compositor transport, not selection or consent windows.

The backend provides DaisyOS permission prompts (including camera/microphone),
file/folder/save pickers, screen/window selection, remote-control consent,
screenshot/color selection, account disclosure, application selection, printing,
background activity, wallpaper changes, launcher installation, USB selection,
global shortcuts and input-capture consent. Appearance settings and sandbox
notifications use DaisyOS theme preferences and its existing notification center.

Interactive requests are accepted only from the current portal frontend's unique bus owner.
Public appearance reads are dispatched to the UI thread and are allowed during frontend startup.
Request.Close cancels pending UI. Session.Close, frontend owner loss and the
visible stop-sharing controls release capture resources. No permission request
is automatically approved. PolicyKit supplies the identities, PAM conversation
and authorization result; the native bridge never substitutes its own success.

## Development installation

```sh
scripts/build-desktop-services.sh
dotnet build src/DaisyOS.Shell/DaisyOS.Shell.csproj --disable-build-servers -m:1
scripts/install-user-desktop-services.sh
scripts/prepare-screen-sharing.sh
```

Run the last two commands as the desktop user in the DaisyOS Wayland session.
The frontend wrapper clears forced GTK/GIO portal use inside the broker itself
to prevent recursive activation while it starts.
The per-user frontend wrapper selects the development portal directory only in
DaisyOS; other desktops retain their system portal directory. Installed sessions
use the system manifest and `daisyos-desktop-services.service`. Packaging stages
the native helpers and their restricted compositor capability desktop entries.

New sessions export `GTK_USE_PORTAL=1`, `GIO_USE_PORTALS=1` and
`QT_QPA_PLATFORMTHEME=xdgdesktopportal`. Applications already running with the
previous environment may need reopening. An application that implements its own
dialog internally does not use the OS portal.

## Verification and remaining boundaries

Run the protocol fixture on a real Wayland desktop, using a private session bus:

```sh
dbus-run-session -- python scripts/test-desktop-portal.py \
  "$PWD/src/DaisyOS.Shell/bin/Debug/net10.0/DaisyOS.Shell"
```

The fixture rejects a spoofed direct request, verifies remote input cannot open
before consent, and cancels dialogs without granting permissions. It does not
enter an account password, print a physical page, open a USB device, or enable
input capture. Those successful operations need interactive acceptance too.

The existing Secret backend remains `kwallet` to preserve application encryption
keys; this change does not migrate existing wallets or replace wallet-unlock UI.
Do not claim the entire OS is free of all KDE UI on this basis alone.

Known protocol limits: remote control currently supports EIS clients, not legacy
NotifyPointer/NotifyKeyboard injection; Clipboard and Email backend interfaces
are not implemented; shortcut reconfiguration/UpdateChoices callbacks and
persistent screen-share restore tokens are not implemented. File filters are
currently limited to extension patterns from the active filter; selectable MIME
filters still need implementation. Wallpaper
requests cannot independently change only the lock screen. Input-capture logical
geometry on fractional-scale outputs needs physical-display verification.

These are explicit remaining compatibility work, not reasons to route an
unsupported interaction to a KDE dialog. Supported desktop portal interfaces route explicitly to DaisyOS and implicit
fallback is disabled; only existing application secret storage has another provider.

## Current validation (2026-10-16)

- Shell build: passed with zero warnings/errors. Native helpers compile successfully.
- Three focused picker tests pass (empty folder, save destination/traversal, cancellation).
- Fourteen live protocol/security assertions pass, including cancellation of the new
  permission, file, account, screenshot, print, USB, shortcut and input-capture dialogs.
- The real public FileChooser opens the DaisyOS window and brings it to the foreground.
  KWin's window bridge now recognizes DesktopServices as an application rather than
  suppressing it as shell chrome.
- A real PolicyKit authorization check displays the DaisyOS authentication window;
  it was cancelled without entering a password or executing an elevated command.
- A real compositor frame reaches a PipeWire consumer; its node disappears after
  the helper closes. This is transport verification, not an approved OBS recording.
- Native/prototype installer tests, ISO profile checks, KWin bridge package checks,
  ShellCheck for the new scripts and `git diff --check` pass.
- In the live session DaisyOS desktop services and the public portal are active;
  the KDE portal, GTK portal and KDE authentication agent are inactive.

The file-picker window was inspected on the actual desktop. Successful password
entry, physical printing, USB access, remote control and an application-approved
screen-sharing session still need interactive acceptance. The entire-OS
"no default components" request remains incomplete at the compatibility boundaries
listed above, particularly existing wallet unlocks.
