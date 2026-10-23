# ROADMAP.md

# Project Roadmap: Arch-Based Desktop Operating System with C# Avalonia UI

## Project Name

Working name: **DaisyOS**

DaisyOS is an Arch Linux-based desktop operating system experience with a custom shell built using **C#**, **.NET**, and **Avalonia UI**.

The goal is not to write a kernel or operating system from scratch. The goal is to create a complete, polished, branded desktop operating system experience on top of Arch Linux, similar in user-facing scope to Windows, macOS, ChromeOS, SteamOS, or other modern desktop operating systems.

The system should boot into a controlled, beautiful, custom desktop environment where the user sees the DaisyOS shell, DaisyOS system apps, approved installed applications, settings, workflows, and OS-branded experiences.

---

# Run Commands

During development:

bash
dotnet restore
dotnet build
dotnet run --project src/DaisyOS.Shell


Development shell shortcuts:


Ctrl+Q   Quit the shell during development
Escape   Close active overlays


Do not remove the development escape shortcuts until a safe recovery/debug path exists.

---

# Core Vision

Build a full desktop OS experience with:

- Custom boot experience
- Custom login / onboarding
- Custom Avalonia desktop shell
- Custom dock / taskbar
- Custom app launcher
- Custom settings app
- Custom file manager
- Custom notification center
- Custom update manager
- Custom power menu
- Custom theme system
- Custom system dialogs
- Controlled app access
- Secure user environment
- Arch-based packaging and updates
- Desktop-first gaming support
- Live ISO and installable system images
- VM-friendly development workflow
- Long-term Wayland compositor integration
- KWin Wayland as the recommended first compositor target for real desktop behavior

This project should start simple and become more complete over time.

The first working milestone is:


A local Avalonia shell app that builds, runs, and provides a usable desktop-style shell prototype.


The second working milestone is:


A bootable Arch-based ISO that starts the DaisyOS Avalonia fullscreen shell automatically.


The long-term milestone is:


A polished custom desktop operating system experience built on Arch Linux.


---

# Development Philosophy

## Principles

- Build incrementally.
- Prefer working prototypes over theoretical architecture.
- Keep the first version simple.
- Do not attempt to write a custom kernel.
- Do not attempt to build everything at once.
- Use Arch Linux as the system base.
- Use Avalonia for the visible shell and DaisyOS system applications.
- Use systemd for services.
- Use safe service wrappers for Linux commands.
- Use QEMU or another VM for ISO and installer testing.
- Never test destructive installer logic on a real machine until VM testing is complete.
- Keep all scripts readable and safe.
- Avoid destructive commands unless absolutely necessary.
- Ask for explicit confirmation before adding disk formatting or installer behavior.
- Prefer self-contained .NET publishing for the shell.
- Treat this repository as both the app source and OS build source.
- Keep gaming features desktop-first and optional where possible.
- Do not turn DaisyOS into a console-only gaming OS.
- Do not start by writing a compositor.
- Use an existing compositor/window manager first.

## Current Compositor Direction

Recommended short-term target:


KWin Wayland


Why:

- Normal desktop windows
- Window moving/resizing/snapping
- Alt+Tab and multitasking
- Multi-monitor support
- Blur/vibrancy effects
- Window shadows
- Window decorations
- Better path for Windows 11-style titlebar/button theming
- XWayland support for legacy Linux apps
- Good desktop-first behavior

Fallback/prototype path:


Xorg + simple window manager


Alternative visual prototype path:


Hyprland


Use Hyprland only for visual experiments such as blur, animation, and compositor effects. KWin is the preferred first serious desktop OS compositor target.

---

# High-Level Architecture


Bootloader
  ↓
Linux kernel
  ↓
systemd
  ↓
KWin Wayland compositor, or Xorg fallback during early prototypes
  ↓
DaisyOS Avalonia Shell
  ↓
DaisyOS system apps and approved desktop apps


The system has three major parts:

## 1. OS Base

The OS base is Arch Linux and includes:

- Linux kernel
- linux-firmware
- systemd
- pacman
- NetworkManager
- PipeWire
- WirePlumber
- Mesa
- Vulkan stack
- Fonts
- Input stack
- Audio stack
- Bluetooth stack
- Basic filesystem layout
- Bootable ISO generation with archiso

## 2. Desktop/Window Layer

The desktop/window layer should initially use an existing compositor/window manager.

Recommended:

- KWin Wayland for the long-term desktop-first target
- Xorg fallback for simpler early ISO boot testing
- Hyprland only for optional visual experiments

Responsibilities of the compositor/window manager:

- App windows
- Window resizing
- Window movement
- Focus
- Alt+Tab
- Snapping
- Fullscreen behavior
- Multi-monitor management
- Window decorations for third-party apps where possible
- Backdrop blur/vibrancy when supported

DaisyOS should not attempt to manage all normal application windows directly from Avalonia.

## 3. User-Facing Shell

The user-facing shell is built with:

- C#
- .NET
- Avalonia UI
- MVVM architecture
- Custom theme system
- Custom UI components
- Shared DaisyOS app window framework
- Services that safely call Linux tools when needed

The shell provides:

- Desktop
- Dock / taskbar
- Launcher
- Search
- Settings
- Quick settings
- Power menu
- Notifications
- File manager
- Update UI
- Lock screen placeholder
- Onboarding
- Gaming settings
- App launching

---

# Repository Structure

The repository should eventually use this structure:


/
  ROADMAP.md
  README.md
  LICENSE
  .gitignore
  global.json

  src/
    DaisyOS.Shell/
      DaisyOS.Shell.csproj
      Program.cs
      App.axaml
      App.axaml.cs
      Views/
      ViewModels/
      Models/
      Services/
      Controls/
      Themes/
      Assets/
      Helpers/
      Platform/
      Resources/

    DaisyOS.Core/
      DaisyOS.Core.csproj
      Models/
      Services/
      Abstractions/
      Helpers/

    DaisyOS.System/
      DaisyOS.System.csproj
      Linux/
      Networking/
      Power/
      Updates/
      Processes/
      Filesystem/
      Audio/
      Bluetooth/
      Security/
      Gaming/
      Diagnostics/

    DaisyOS.Settings/
      DaisyOS.Settings.csproj

    DaisyOS.FileManager/
      DaisyOS.FileManager.csproj

    DaisyOS.Welcome/
      DaisyOS.Welcome.csproj

    DaisyOS.Software/
      DaisyOS.Software.csproj

    DaisyOS.Monitor/
      DaisyOS.Monitor.csproj

    DaisyOS.GameCenter/
      DaisyOS.GameCenter.csproj

    DaisyOS.Installer/
      DaisyOS.Installer.csproj

  os/
    archiso/
      DaisyOS/
        packages.x86_64
        profiledef.sh
        pacman.conf
        airootfs/

    packages/
      desktop-packages.txt
      gaming-packages.txt
      minimal-packages.txt

    systemd/
      DaisyOS-shell.service
      DaisyOS-firstboot.service
      DaisyOS-updater.service

    configs/
      kwin/
      xorg/
      network/
      security/
      users/
      branding/
      gaming/

  scripts/
    setup-dev.sh
    publish-shell.sh
    copy-shell-to-iso.sh
    build-iso.sh
    clean-iso.sh
    run-qemu.sh
    format-check.sh
    test.sh

  docs/
    architecture.md
    shell.md
    iso-build.md
    testing.md
    installer.md
    security.md
    theming.md
    packaging.md
    update-system.md
    troubleshooting.md
    ai-agent-guidelines.md
    gaming.md
    compositor.md
    default-apps.md
    window-controls.md

  assets/
    branding/
    wallpapers/
    icons/
    sounds/


If the current repository is smaller, create this structure gradually. Do not move files unnecessarily unless it improves maintainability.

---

# Progress Legend

Use this status format throughout the roadmap:


[ ] Not started
[-] In progress
[x] Done
[!] Blocked
[~] Needs review


When completing work, update the checklist and add notes under the relevant section.

---

# Phase 0: Repository and Development Setup

## Goal

Create a clean development environment for building the Avalonia shell and Arch-based ISO.

## Checklist

- [x] Create repository structure.
- [x] Add `.gitignore`.
- [x] Add `README.md`.
- [x] Add `ROADMAP.md`.
- [x] Add `docs/` folder.
- [x] Add `scripts/` folder.
- [x] Add `src/` folder.
- [x] Add `os/` folder.
- [x] Add `assets/` folder.
- [x] Add solution file.
- [x] Add .NET SDK version pinning using `global.json`.
- [x] Add basic build instructions.
- [x] Add basic run instructions.
- [x] Add basic VM test instructions.
- [x] Add contribution notes for AI coding agents.
- [x] Add warning that installer/disk actions must not be run on a real machine without explicit confirmation.

## Notes

- 2026-07-02: Bootstrap scaffolding created.
- 2026-07-02: Later check found `.NET SDK 10`, `mkarchiso`, QEMU, and ShellCheck available on PATH.
- 2026-07-03: Project builds locally with .NET SDK 10.

## Development Tools

The development environment should support:

- Linux host system
- VS Code
- .NET SDK 10
- Avalonia templates, optional
- Git
- archiso
- QEMU
- virt-manager or GNOME Boxes
- ShellCheck
- systemd unit file editing

## Scripts to Create

### `scripts/setup-dev.sh`

Purpose:

Check required development dependencies.

Should check for:

- `dotnet`
- `git`
- `mkarchiso`
- `qemu-system-x86_64`
- `shellcheck`
- `bash`
- `pacman`
- `code`, optional

Do not blindly install packages without clear user confirmation.

Important implementation note:

`mkarchiso -V` may not be supported. Check `mkarchiso` with:

bash
command -v mkarchiso


### `scripts/test.sh`

Purpose:

Run project tests.

### `scripts/format-check.sh`

Purpose:

Check formatting for C# and shell scripts.

---

# Phase 1: Avalonia Shell MVP

## Goal

Create a working Avalonia app that looks and behaves like the beginning of a desktop operating system shell.

The app should run normally on a Linux desktop during development.

## Checklist

- [x] Create Avalonia shell project.
- [x] Ensure project builds.
- [x] Ensure project runs with `dotnet run`.
- [x] Create fullscreen shell window.
- [x] Remove normal window decorations.
- [x] Add temporary development escape shortcut.
- [x] Add home desktop view.
- [x] Add wallpaper/background area.
- [x] Add bottom system bar.
- [x] Add bottom dock or taskbar.
- [x] Add clock.
- [x] Add launcher button.
- [x] Add settings button.
- [x] Add power button.
- [x] Add mock notification icon.
- [x] Add basic app tiles.
- [x] Add placeholder app launcher panel.
- [x] Add placeholder settings panel.
- [x] Add placeholder power menu.
- [x] Add basic keyboard navigation.
- [x] Add basic mouse interaction.
- [x] Add responsive scaling.
- [x] Add light theme.
- [x] Add dark theme.
- [x] Add glass-style UI components.
- [x] Add rounded card components.
- [x] Add reusable button styles.
- [x] Add icon system.
- [x] Add font resources.
- [x] Add README instructions for running the shell.

## Notes

- 2026-07-02: Shell MVP was hand-coded without Avalonia templates.
- 2026-07-02: `dotnet build` succeeds.
- 2026-07-02: Keyboard navigation currently covers `Escape` to close overlays and `Ctrl+Q` to quit during development.
- 2026-07-02: Icon support began with placeholders and should become a real icon asset/font system.
- 2026-07-02: Default styling was changed from dark neon/futuristic to a softer Windows 11 plus macOS-inspired light glass look.
- 2026-07-03: Shell now uses DaisyOS project names, SVG status icons, persisted theme/volume settings, and a PipeWire volume bridge through `wpctl`.
- 2026-07-05: Launcher search filters mock apps and quick links live, with an empty state when nothing matches.
- 2026-07-15: Closed the MVP presentation contract with keyboard-reachable
  shell surfaces, adaptive 1024x640 through 1600x900 rendering, compact
  Settings/Files layouts, shared semantic button themes, and the Material
  Symbols plus app-icon source system. The headless shell demonstration gate
  now renders and validates these contracts from the production resource graph.

## Main Shell Features

The first shell should include:


Desktop background
Top bar
Clock
Status indicators
Dock/taskbar
Launcher
Settings shortcut
Power menu
Basic app cards


## Design Direction

The visual style should feel:

- Modern
- Calm
- Clean
- Premium
- Glassy
- Rounded
- Smooth
- Minimal
- Desktop-native
- Inspired by modern OS design, but not a copy of Windows or macOS

Do not use Apple, Microsoft, Google, or other proprietary branding/assets.

## Required Views

Create or maintain these initial views:

- `ShellView`
- `DesktopView`
- `SystemBarView`
- `DockView`
- `LauncherView`
- `SettingsView`
- `PowerMenuView`
- `NotificationCenterView`

## Required ViewModels

Create or maintain these initial view models:

- `ShellViewModel`
- `DesktopViewModel`
- `SystemBarViewModel`
- `DockViewModel`
- `LauncherViewModel`
- `SettingsViewModel`
- `PowerMenuViewModel`
- `NotificationCenterViewModel`

## Required Services

Create or maintain service abstractions:

- `IClockService`
- `IPowerService`
- `INetworkStatusService`
- `IAudioService`
- `IAppLauncherService`
- `ISettingsService`
- `INotificationService`
- `IThemeService`

Initial implementations may be mock implementations.

---

# Phase 2: System Services Integration

## Goal

Connect the Avalonia shell to real Linux functionality through safe service wrappers.

## Checklist

- [x] Create `DaisyOS.System` project.
- [x] Add process execution helper.
- [x] Add safe command runner.
- [x] Add logging.
- [x] Add power service.
- [x] Add reboot action.
- [x] Add shutdown action.
- [x] Add suspend action if supported.
- [x] Add network status service.
- [x] Add Wi-Fi list support through NetworkManager.
- [x] Add connect-to-Wi-Fi support.
- [x] Add disconnect support.
- [x] Add audio status support.
- [x] Add volume control support.
- [x] Add Bluetooth status support.
- [x] Add battery status support.
- [x] Add storage status support.
- [x] Add CPU/RAM usage status support.
- [x] Add display information support.
- [x] Add user session information.
- [x] Add app process launching.
- [~] Add service error handling.
- [x] Add permission checks.
- [x] Add mock mode for development.
- [~] Add real mode for ISO/system builds.

## Notes

- 2026-07-02: Real Linux wrappers exist for power and basic NetworkManager status, but the shell may still wire mock services by default.
- 2026-07-03: Added `IAudioService` and `LinuxAudioService` using `wpctl` for default sink volume read/write. The system bar volume slider now syncs with system volume.
- 2026-07-04: Added a small shell log service and wired safe command failures/timeouts to `~/.local/state/DaisyOS/shell.log` or `$XDG_STATE_HOME/DaisyOS/shell.log`.
- 2026-07-04: Added battery status support from `/sys/class/power_supply` and wired the system bar battery tooltip/icon to live status.
- 2026-07-05: Added opt-in real shell service mode through `DaisyOS_SHELL_SERVICES=real` or `--real-services`, plus conservative `.desktop` app discovery/launching.
- 2026-07-06: Power menu and launcher power actions call the safe power service and surface action results through notifications.
- 2026-07-06: Added read-only CPU and memory status from `/proc` and surfaced it in Settings.
- 2026-07-06: Added read-only display status through `xrandr --query` and surfaced it in Settings.
- 2026-07-06: Added read-only storage, user session, and system capability/permission status to Settings.
- 2026-07-06: Added read-only Wi-Fi network listing through NetworkManager and surfaced it in Settings.
- Missing command failures should remain non-fatal.

## Safety Requirements

All system commands must be handled carefully.

- [x] Never pass unsanitized user input to shell commands.
- [x] Prefer direct process arguments over shell string execution.
- [x] Capture stdout.
- [x] Capture stderr.
- [x] Handle timeouts.
- [x] Handle missing commands.
- [x] Log failures.
- [~] Return user-friendly errors to the UI.
- [x] Do not run destructive commands without explicit UI confirmation.
- [x] Do not implement disk formatting yet.

## Example Services

Power service may wrap:


systemctl poweroff
systemctl reboot
systemctl suspend


Network service may wrap:


nmcli device status
nmcli radio wifi
nmcli device wifi list
nmcli device wifi connect


Audio service may wrap:


wpctl get-volume @DEFAULT_AUDIO_SINK@
wpctl set-volume @DEFAULT_AUDIO_SINK@


---

# Phase 3: Arch ISO Build System

## Goal

Create a custom Arch ISO profile that includes the published Avalonia shell and boots into it automatically.

## Checklist

- [x] Add `os/archiso/DaisyOS` profile.
- [x] Base profile on Arch `releng`.
- [x] Add `packages.x86_64`.
- [x] Add `profiledef.sh`.
- [x] Add custom `airootfs`.
- [x] Add shell app destination at `/opt/DaisyOS/shell`.
- [x] Add systemd service for shell startup.
- [x] Add user creation script.
- [x] Add auto-login setup.
- [x] Add NetworkManager enablement.
- [x] Add PipeWire packages.
- [x] Add Mesa packages.
- [x] Add Xorg packages.
- [x] Add KWin Wayland prototype package plan.
- [x] Add fonts.
- [~] Add branding files.
- [x] Add wallpaper.
- [x] Add custom issue/motd if needed.
- [x] Add build script.
- [x] Add QEMU test script.
- [x] Add clean script.
- [x] Document ISO build process.
- [x] Document ISO test process.
- [x] Verify ISO builds.
- [x] Verify ISO boots in QEMU.
- [x] Verify shell starts automatically.
- [x] Verify shell restarts if it crashes.
- [x] Add a source-bound bootable live-ISO alpha acceptance gate.
- [x] Pass the complete alpha gate with a fresh current-checkout ISO.

## Notes

- 2026-07-02: ISO profile is a minimal first skeleton and has not been compared against a fresh Arch `releng` copy.
- 2026-07-02: `mkarchiso` and `qemu-system-x86_64` are available, but ISO build and boot verification were not run during the shell visual restyle.
- 2026-07-13: Modernized and validated the profile against the installed archiso layout, added explicit BIOS/UEFI boot configuration and live initramfs hooks, and built a 1.5 GiB hybrid ISO. Its BIOS path reaches `graphical.target` under QEMU TCG, auto-logs in, starts Xorg, launches the staged shell, records failures, and exercises the bounded restart supervisor. The emulated display remains black while the shell process runs, so visible desktop interaction and UEFI runtime boot remain unverified.
- 2026-07-14: Isolated the black framebuffer to repeated .NET JIT work and a native allocator abort under QEMU TCG. A composite ReadyToRun production publish removed that startup failure. The freshly rebuilt 1.5 GiB standalone ISO passed the guarded headless smoke test with 14,668 framebuffer colors, then passed interactive BIOS checks for fullscreen onboarding, the desktop, Launcher, Settings, the power menu, NetworkManager, and PipeWire. Guest-agent diagnostics remain opt-in so physical machines and VMs without the channel do not wait for a missing device.
- 2026-07-14: Advanced the firmware and power lifecycle gates. The same ISO reaches `graphical.target` through its UEFI systemd-boot entry and one guarded OVMF/TCG run produced a stable 14,668-color frame with standard VGA. Separate diskless VM runs opened the real power menu and verified both Restart and Shut down. Firmware-aware smoke tooling now guards BIOS and UEFI boot, visible rendering, and post-render shell stability; conditional guest-agent activation preserves fast physical/no-channel boot while enabling disposable-VM setup; and unexpected exits record firmware, renderer, kernel, status, signal, and bounded dump diagnostics. A fresh repeat BIOS run caught the native `free(): invalid pointer` status-134 abort even with standard VGA, followed by a live but blank supervised retry. Therefore both firmware entries are bootable and the power actions are proven, but repeatable TCG shell rendering remains open. The crash-dump helper permission contract was corrected so the next rebuilt ISO can retain the native dump.
- 2026-07-15: Added `scripts/validate-live-iso-alpha.sh` as the authoritative alpha exit gate. It rejects stale images, fingerprints the current shell source, verifies the production shell hash inside SquashFS, runs repository checks, requires BIOS and UEFI rendering plus live NetworkManager/PipeWire/WirePlumber, and exercises Restart and Shut down in diskless VMs. Completion remains pending until a newly built image passes every gate in one current-checkout run.
- 2026-07-16: **Bootable live-ISO alpha reached 100%.** One uninterrupted authoritative run accepted `out/DaisyOS-2026.07.16-x86_64.iso` (1,571,160,064 bytes; SHA-256 `dfe2ac9ee2083cc9520021766cd0cb007182553430cf533624371a1cee158621`). The source-bound production payload passed all 282 repository tests and profile checks; BIOS and UEFI each rendered a 14,574-color desktop and held stable for 60 seconds; the supervisor recovered from the intentional status-143 shell stop; and separate diskless VMs completed Restart and Shut down through the real launcher power menu. Immutable evidence is recorded in `artifacts/live-iso-alpha/report.md`. This certifies only the non-installing VM-tested live alpha, not the mock installer, broad hardware support, package publication, secure updates, or production security.
- 2026-07-16: Promoted KWin Wayland, XWayland, desktop portals, and the authentication agent into the next live profile. Startup now attempts direct DRM composition, falls back to software-composited KWin over Xorg, and exposes the old bare-Xorg shell only as recovery. Wallpaper rendering, the Updates symbol, hidden auto-boot menus, UEFI-first QEMU launch, standard-VGA aspect safety, and GTK zoom-to-fit were corrected. The repository and isolated software-composited nested-KWin gates pass; a rebuilt ISO must still prove direct or compatibility KWin startup under both firmware modes before this candidate replaces the accepted alpha above.
- 2026-07-16: Rejected the first 1.8 GiB compositor image after visual evidence showed that the old full-screen GPU vibrancy surface could leave most of the KWin framebuffer black while the previous color-count gate accepted a narrow wallpaper strip. Removed that desktop-wide OpenGL host, retained compositor-native blur requests on Settings and Files, and strengthened the VM gate to require at least 20% non-black full-frame coverage plus a second visual check after recovery/stability. Corrected payload runs rendered the complete 1280x800 wallpaper, Dock, and System Bar through UEFI/KWin; opened Settings and Files as decorated, translucent KWin-managed windows; showed the corrected Updates icon; and held the Files frame for 120 seconds with 98.6% non-black coverage. The production ReadyToRun payload is republished and staged. A rebuilt ISO and uninterrupted full gate remain required.
- 2026-07-16: Restored fixed-panel vibrancy without restoring the unsafe full-screen OpenGL child. A single capped sharp/blurred wallpaper texture pair is cached per wallpaper; each Dock, System Bar, Launcher, and flyout surface crops and GPU-composites only its own backdrop region, with continuous sharp-to-blurred blending from the Appearance slider. A UEFI/KWin payload run visibly confirmed wallpaper color and blur through the Launcher while the full desktop stayed intact. Real-session theme startup and the System Bar toggle now synchronize DaisyOS plus KWin decorations; Dark remains the first-run default unless onboarding persists Light.
- 2026-07-16: Retired the interim wallpaper-sampled vibrancy path and all remaining GPU-era resources. Every shell/application backdrop now delegates blur to KWin, while `KWinBlurSettings.axaml` is the single global tuning contract for compositor strength, saturation, noise, and shared material tint.
- Do not start installable-system work until the live ISO flow is stable.

## Required Packages

Initial Xorg-based ISO package list should include at least:

base
linux
linux-firmware
systemd
networkmanager
pipewire
pipewire-pulse
wireplumber
alsa-utils
mesa
libglvnd
xorg-server
xorg-xinit
xorg-xrandr
xorg-xset
noto-fonts
ttf-dejavu
sudo
vim
nano
less
bash-completion


Only add more packages when needed.

## Future KWin Wayland Prototype Packages

Research and test before adding to default ISO:


kwin
plasma-workspace
xdg-desktop-portal
xdg-desktop-portal-kde
qt6-wayland
xorg-xwayland


KWin integration must be tested in a VM before becoming the default shell session.

## ISO Scripts

### `scripts/publish-shell.sh`

Should:

- Build Avalonia shell.
- Publish as self-contained Linux x64 binary.
- Output to `publish/linux-x64`.

### `scripts/copy-shell-to-iso.sh`

Should:

- Remove old shell files from ISO profile.
- Copy new published shell into `airootfs/opt/DaisyOS/shell`.
- Make executable files executable.

### `scripts/build-iso.sh`

Should:

- Check that `mkarchiso` exists using `command -v mkarchiso`.
- Build ISO from the profile.
- Output ISO to `out/`.

### `scripts/run-qemu.sh`

Should:

- Find latest ISO.
- Boot it in QEMU.
- Use sensible VM defaults.
- Avoid destructive disk operations.

### `scripts/clean-iso.sh`

Should:

- Remove archiso work directory.
- Optionally remove old ISO outputs.
- Must not remove unrelated files.

---

# Phase 4: Shell Startup and Session Management

## Goal

Create a reliable startup flow that boots the system into the DaisyOS shell.

## Checklist

- [x] Create dedicated live user named `daisyos`.
- [x] Configure auto-login for live ISO.
- [x] Start Xorg for early ISO testing.
- [x] Launch Avalonia shell.
- [x] Ensure shell runs fullscreen.
- [x] Ensure shell is restarted on crash.
- [x] Ensure logging is available.
- [x] Ensure environment variables are set.
- [x] Ensure permissions allow graphics/input/audio/network access.
- [x] Add development mode.
- [x] Add production mode.
- [x] Add fallback shell option for debugging.
- [x] Add safe recovery instructions.
- [x] Add future KWin Wayland session plan.

## Notes

- 2026-07-02: The shell window is fullscreen in the Avalonia app.
- 2026-07-13: Added the bounded `daisyos-session` supervisor, file/journal logging, production-only shortcut restrictions, and persistent xterm recovery after three failed launches. Repository tests verify the restart and fallback state machine; ISO/QEMU boot verification remains separate. See `docs/session-startup.md`.

## systemd Requirements

Create a systemd service similar to:

ini
[Unit]
Description=DaisyOS Avalonia Shell
After=systemd-user-sessions.service network-online.target
Wants=network-online.target

[Service]
User=daisyos
WorkingDirectory=/opt/DaisyOS/shell
Environment=DISPLAY=:0
Environment=HOME=/home/daisyos
ExecStart=/usr/bin/xinit /usr/local/bin/daisyos-session -- :0 -nolisten tcp
Restart=on-failure
RestartSec=2

[Install]
WantedBy=multi-user.target


This may need adjustment during testing.

## Startup Debugging

Add documentation for:

- Viewing logs with `journalctl`
- Restarting the shell service
- Checking Xorg logs
- Running the shell manually
- Verifying executable permissions
- Checking missing libraries
- Booting to fallback terminal/session

---

# Phase 5: Desktop Shell Features

## Goal

Turn the MVP shell into a usable desktop environment.

## Checklist

- [x] Add polished desktop layout.
- [x] Add wallpaper support.
- [x] Add wallpaper picker.
- [x] Add dock/taskbar.
- [x] Add pinned apps.
- [x] Add running app indicators.
- [x] Add app launcher.
- [x] Add search.
- [x] Add system tray/status area.
- [x] Add notification center.
- [x] Add quick settings panel.
- [x] Add calendar flyout.
- [x] Add power menu.
- [x] Add lock screen.
- [x] Add idle lock timer (5 min).
- [x] Add user profile menu.
- [x] Add keyboard shortcuts.
- [x] Add accessibility basics.
- [x] Add animations.
- [x] Add settings persistence.
- [x] Add error toasts.
- [x] Add loading states.
- [x] Add empty states.
- [x] Add offline states.
- [x] Add compositor/window-manager integration notes.

Note: 2026-07-05: The top-bar clock now opens a calendar flyout with month navigation and a today shortcut.
Note: 2026-07-12: Replaced the mismatched native calendar chrome with a compact DaisyOS month grid featuring localized week order, restrained adjacent-month dates, clear today/selection states, and arrow/Page Up/Page Down/Home keyboard navigation.
Note: 2026-07-06: User-added Dock pins are persisted in settings and restored on shell startup.
Note: 2026-07-13: Replaced the unused Dock auto-hide preference with a live, persisted position selector. The Dock now supports the left, right, top, and bottom edges with edge-aware orientation, separators, running indicators, scaling, margins, and tooltip placement.
Note: 2026-07-10: Appearance settings include bundled wallpaper previews plus a native file picker for user-selected images.
Note: 2026-07-10: Stabilization removed duplicate clock/status polling, stopped Settings metrics while its window is closed or minimized, reduced inactive media discovery to a low-frequency poll, and completed root/service disposal.
Note: 2026-07-10: The responsive desktop now materializes its full cell matrix, shows subtle theme-aware cell outlines, and exposes every empty cell as a valid icon drop target.
Note: 2026-07-12: A shell-wide UI/UX refinement introduced consistent loading, empty, success, and error presentation; larger interaction targets; clearer action hierarchy; improved menus and tooltips; a discoverable lock-screen action; and a functional close action in Updates.
Note: 2026-07-12: Added a top-bar user profile menu backed by live session information, with working Account Settings, Lock Screen, and Power Options actions plus clear unavailable messaging for future user switching and sign-out.
Note: 2026-07-12: Added a dedicated Accessibility settings page with persisted high-contrast and Reduce Motion controls plus keyboard-navigation guidance. All XAML transitions now consume live motion tokens, and launcher/desktop code animations bypass movement when reduced motion is enabled.
Note: 2026-07-13: Closed the shell-state contract across Launcher, Quick Search, Files, Updates, Notifications, Settings, and Welcome. Loading, empty, offline, unavailable, and error states now have explicit bindings; Network distinguishes offline from an unavailable backend; Welcome uses state-specific icons and failure styling; error and critical toasts bypass quiet modes; and file, desktop, launcher, power, update, and network failures no longer expose exception text or command output. Automated contract tests protect these behaviors.
Note: 2026-07-15: Completed the polished shell demonstration contract in
`docs/shell-demo.md`. Network and Bluetooth status controls now open real
popover surfaces and route to their Settings page; Sound, Gaming, Profile,
Calendar, Notifications, Updates, Power, Launcher, Search, Lock, Settings, and
Files share a 22-frame interaction/rendering gate. Overlay replacement is
atomic, lock suppresses global shortcuts, icon-only controls expose accessible
names, and Files now has an honest, tested 720x500 compact minimum. This
milestone does not claim installer, native Wayland, Secure Boot, encryption, or
broad hardware readiness.

## Desktop Components

Required components:

- `DesktopSurface`
- `Dock`
- `Taskbar`
- `SystemBar`
- `Launcher`
- `QuickSettings`
- `NotificationCenter`
- `CalendarPanel`
- `PowerMenu`
- `UserMenu`
- `StatusIndicators`

## Keyboard Shortcuts

Add support for:


Super / Windows key: open launcher
Alt + Tab: switch between DaisyOS-tracked apps; compositor integration will add external windows
Ctrl + Alt + Del: power/security menu
Escape: close active overlay
Super + S: settings
Super + L: lock screen
Super + Space: search


Note: 2026-07-13: Implemented Phases 1 and 2 of system-wide quick search. The centered Super+Space overlay and Launcher share one cancellable ranked service for applications, settings, calculations, folders, and files. Phase 2 adds a persistent user-data SQLite/FTS5 metadata index, background batched scans of standard home folders, configurable inclusion/exclusion policy, corruption recovery, and bounded 500 ms filesystem-event coalescing with overflow reconciliation. Query-specific learning, recent/pinned indexed results, highlighting, and diagnostics remain Phase 3 work.
Note: 2026-07-13: Implemented Phase 3 search intelligence: bounded persistent query-specific and general usage learning, recent and pinned empty-query suggestions, centralized ranking with type/recency/frequency/path-depth weighting, matched-name highlighting in Quick Search and Launcher, and private in-process timing/candidate diagnostics. Exact matches retain priority over learned preferences.
Note: 2026-07-13: Began Phase 4 with an opt-in, fully local content-extraction pipeline. Settings now controls background content indexing and clearly reports its privacy behavior. Supported bounded plain-text documents, source files, and PDFs become FTS-searchable; sensitive filenames and oversized content are rejected. PDF work is capped by size, page count, and extracted characters. Image files contribute dimensions, orientation, and format metadata without OCR or pixel analysis. Extractors use a pluggable interface so OCR and other providers can be added independently later.
Note: 2026-07-13: Added Spotlight-style query interpretation without cloud services: conversational action/filler words are ignored, common location typos are corrected, media/document/archive intents expand to metadata synonyms, strict multi-term search falls back to broad and typo-recovery candidates, and natural time/size phrases such as “modified today”, “yesterday”, “recent”, and “large files” become database filters. Quick Search now stays collapsed until the user enters a query and never shows idle recent items.
Note: 2026-07-13: Corrected localized home-folder discovery by reading `user-dirs.dirs` instead of assuming English directory names. Exact literal terms are now always queried first without filtering; natural-language interpretation runs only after the literal query has no results. This fixes direct filename lookup such as `gear.png` under localized Downloads paths such as `~/Lataukset`.
Note: 2026-07-13: Search source completion is now persisted in SQLite, so completed file indexes survive shell restarts instead of being deleted and rebuilt. Live bounded watchers maintain file creates, changes, renames, and removals; six-hour reconciliation repairs changes made while the shell was offline. Application directories under user, local, and system data roots are monitored with debounced catalog replacement, so installed, updated, and removed desktop applications update search without restarting the shell.

During development, include an emergency shortcut to quit the shell.

---

# Phase 6: Settings App

## Goal

Build a complete settings experience similar to Windows Settings or macOS System Settings, while keeping original DaisyOS branding and behavior.

## Checklist

- [x] Create settings home screen.
- [x] Add sidebar navigation.
- [x] Add search settings.
- [x] Add network settings.
- [x] Add Wi-Fi settings with connect/disconnect.
- [x] Add Bluetooth settings with toggle.
- [x] Add display settings.
- [x] Add sound settings.
- [x] Add appearance settings.
- [x] Add wallpaper settings.
- [x] Add dock/taskbar settings.
- [x] Add window/titlebar settings.
- [x] Add compositor settings placeholder.
- [x] Add notifications settings.
- [x] Add power/battery settings.
- [x] Add storage settings.
- [x] Add users settings.
- [x] Add privacy settings.
- [x] Add security settings.
- [x] Add updates settings.
- [x] Add gaming settings.
- [x] Add GPU detection in Gaming.
- [x] Add live Vulkan/GameMode/MangoHud/Gamescope detection.
- [x] Add about device page.
- [x] Add developer settings.
- [x] Add reset/recovery settings placeholder.

## Notes

- 2026-07-06: Settings expanded into real sidebar pages for System, Appearance, Sound, Network, Power & Battery, Dock, Gaming, and About using existing services only. Network and battery pages are status-only; Gaming is an honest placeholder for later backend work.
- 2026-07-06: Settings search now switches the main pane to searchable results and routes matches to the relevant Settings page.
- 2026-07-10: Added an Updates settings page that explains the read-only backend and opens the existing package update manager.
- 2026-07-10: Added a dedicated read-only Storage page showing the active filesystem mount, capacity usage, and service detail.
- 2026-07-12: Completed the Settings navigation with Users, Privacy, Security, Windows, Developer, and Recovery pages. Pages use live read-only session, permission, capability, display, and runtime data; unsafe or compositor-dependent mutations are clearly marked unavailable.

## Settings Persistence

User settings should be stored in:


~/.config/DaisyOS/settings.json


System-wide settings may be stored in:


/etc/DaisyOS/settings.json


Use a typed settings model.

Do not store secrets in plain JSON.

---

# Phase 7: App Launcher and App Management

## Goal

Allow users to launch installed and approved applications from the custom shell.

## Checklist

- [x] Create app model.
- [x] Create app registry.
- [x] Load `.desktop` files.
- [x] Parse app names, icons, categories, and commands.
- [x] Show app grid.
- [x] Add app search.
- [x] Add app categories.
- [x] Add pinned apps.
- [x] Add recent apps.
- [x] Add favorite apps.
- [x] Launch apps safely.
- [x] Show running app indicators.
- [x] Add basic process tracking.
- [x] Add app details page.
- [x] Add uninstall placeholder.
- [x] Add app permissions placeholder.
- [ ] Add restricted app mode.

## Notes

- 2026-07-09: `MockAppLauncherService` now discovers real `.desktop` apps from the filesystem, giving the launcher a real app model/registry backed by parsed name/icon/category/command data.
- 2026-07-11: Added persisted launcher favorites and an app-details surface with launch type, categories, permission scope, dock controls, and a safe non-operational uninstall placeholder.
- Real app launching goes through the existing safe command runner (see Phase 2), so "launch apps safely" is covered by that safety layer rather than new Phase 7-specific code.
- Running indicators and basic process tracking cover apps launched by DaisyOS. Compositor integration remains future work for authoritative tracking of externally launched windows.
- 2026-07-10: Launcher applications are grouped by normalized desktop-entry categories, category names participate in search, and a bounded persisted recent-app list is shown separately from the main catalog.

## Approved Apps

For a controlled OS, the shell should eventually support an allowlist:


/etc/DaisyOS/allowed-apps.json


Allowlist fields:

json
{
  "apps": [
    {
      "id": "org.example.App",
      "name": "Example App",
      "command": "/usr/bin/example",
      "icon": "example",
      "allowed": true
    }
  ]
}


Do not block normal desktop development on allowlist enforcement.

Note: 2026-07-12: A report-only app policy service reads `/etc/DaisyOS/allowed-apps.json`, trusts internal apps, and reports allowed, denied, unlisted, missing, malformed, and unavailable states without filtering or blocking applications. Enforcement remains future security work.

---

# Phase 8: File Manager

## Goal

Create a custom Avalonia file manager.

## Checklist

- [x] Create file manager app/view.
- [x] Show home directory.
- [x] Show common folders.
- [x] Show files and folders.
- [x] Add list view.
- [x] Add grid view.
- [x] Add breadcrumbs.
- [x] Add path bar.
- [x] Add back/forward navigation.
- [x] Add copy.
- [x] Add paste.
- [x] Add rename.
- [x] Add delete to trash.
- [x] Add permanent delete only with confirmation.
- [x] Add new folder.
- [x] Add file properties.
- [x] Add open with.
- [x] Add search.
- [x] Add hidden files toggle.
- [x] Add mounted drives view outside the pinned quick-link list.
- [x] Add safe error handling.
- [x] Add permissions display.
- [x] Add preview pane placeholder.

Note: 2026-07-10: Files now supports inline rename with validation and conflict handling, plus a Unix-aware hidden-file toggle. Existing home/common-folder navigation, list/grid views, search, copy/paste, trash, folder creation, properties, and error notifications were reviewed and marked complete.
Note: 2026-07-10: Added clickable breadcrumbs, editable path navigation, filtered mounted locations, Unix permission details, and bounded previews for common text files.
Note: 2026-07-10: Added permanent-delete and empty-Trash confirmation flows; destructive actions remain unavailable until the user confirms the modal prompt.
Note: 2026-07-10: Simplified the Files sidebar to six default quick links (Desktop, Downloads, Documents, Pictures, Videos, and Music). Locations can be pinned from the toolbar, removed from the sidebar context menu, and are persisted in user settings.
Note: 2026-07-10: Rebuilt the Files header around reusable path and search fields, square shell-style navigation/actions, explicit list/grid layouts, and small/medium/large item sizing. Item context menus now expose consistent open, copy, paste, rename, sidebar pin/unpin, trash, and properties actions in both layouts.
Note: 2026-07-10: Completed the Open With flow with MIME-aware application discovery, a bounded application chooser, unavailable/empty states, and safe `gio launch` execution.
Note: 2026-07-10: Added a separate Devices section backed by the storage service; ready system and removable mounts are shown independently from persisted quick links with available-capacity badges.

## Safety

The file manager must:

- Avoid deleting system files accidentally.
- Confirm destructive actions.
- Respect user permissions.
- Avoid running executable files without confirmation.
- Handle permission errors gracefully.

---

# Phase 9: Notifications

## Goal

Implement a custom notification system.

## Checklist

- [x] Create notification model.
- [x] Create notification service.
- [x] Add notification toasts.
- [x] Add notification center.
- [x] Add dismiss action.
- [x] Add clear all action.
- [x] Add notification history.
- [x] Add notification categories.
- [x] Add app icons.
- [x] Add urgent notifications.
- [x] Add notification sounds.
- [x] Add do-not-disturb mode.
- [x] Add do-not-disturb while gaming.
- [x] Add settings integration.
- [x] Add system event notifications.
- [x] Add update notifications.
- [x] Add network notifications.
- [x] Add battery notifications.

## Notes

- 2026-08-20: Expanded Phase 9 Notification Center & Actionable Toast Banners with slide-in toast overlay (`NotificationToastHost`), auto-dismiss timers, NotificationCenterView drawer in SystemBar calendar flyout, unread badge indicators, Quick Settings Focus Mode DND synchronization, action button triggers, and automated tests.
- 2026-07-11: Removed dismissed-notification history in favor of one active-only panel, unified panel/toast presentation, and added urgency, sounds, gaming quiet mode, plus deduplicated network, battery, and update events.

- 2026-07-06: Notification center now updates from service changes, supports per-notification dismiss, clear all, timestamps, and an empty state. Toast popups and persisted history are still future work.
- 2026-07-06: Added shell notification toasts, persisted toast enablement, a Do Not Disturb toggle, and a Settings notifications page. Do Not Disturb suppresses toast popups while keeping notifications in the center.
- 2026-07-10: Added persisted dismissed-notification history with active/history tabs, bounded retention, and separate clear actions.

---

# Phase 10: Theme and Design System

## Goal

Create a complete visual design system for the OS.

## Checklist

- [x] Define color tokens.
- [x] Define typography.
- [x] Define spacing scale.
- [x] Define corner radius scale.
- [x] Define elevation/shadows.
- [x] Define glass material styles.
- [x] Define button styles.
- [x] Define dock button styles.
- [x] Define card styles.
- [x] Define input styles.
- [x] Define list styles.
- [x] Define menu styles.
- [x] Define dialog styles.
- [x] Define notification styles.
- [x] Define app window/titlebar styles.
- [x] Define animation durations.
- [x] Define easing curves.
- [x] Add light theme.
- [x] Add dark theme.
- [x] Add high contrast theme placeholder.
- [x] Add accent color support.
- [ ] Add wallpaper-based color placeholder.
- [x] Add theme persistence.
- [x] Add theme preview in settings.

Note: Phase 1 already shipped a working light/dark theme with persistence as part of the shell MVP. This phase tracks turning that into a formal, documented design system (tokens, scales, and per-component style definitions) rather than starting theming from zero.
Note: 2026-07-10: Enabled Avalonia FluentTheme at normal density, added semantic Fluent control and typography layers, migrated calendar UI to the standard control, and removed styling-only dock renderers. Search, volume, and top-bar flyout wrappers remain because they provide established reusable behavior.
Note: 2026-07-10: Added shared semantic spacing and corner-radius resources in `OSLayout.axaml`; base and primary button geometry now consume the radius scale as the first component migration.
Note: 2026-07-12: Added dedicated dialog, notification, and app-window style layers plus shared motion duration/easing tokens. Files destructive confirmations and shell windows now consume the semantic styles; native window-manager chrome remains compositor-owned.
Note: 2026-07-12: Appearance settings now persist seven accent choices and a functional high-contrast mode. Accent changes update semantic focus, selection, link, and primary-action colors; high contrast replaces translucent surfaces with an opaque black, white, and yellow palette.

## Icon System

DaisyOS should support:

- Material Symbols Rounded font for all system UI icons, with heavier treatment for active and status icons
- SVG icons for scalable app/system icons
- PNG assets only for logos or complex images

Recommended icon paths:


src/DaisyOS.Shell/Assets/Icons/
src/DaisyOS.Shell/Assets/Icons/MaterialSymbolsRounded-VariableFont_FILL,GRAD,opsz,wght.ttf


Use Avalonia resource paths:


avares://DaisyOS.Shell/Assets/Icons/example.svg


---

# Phase 11: Update System

## Goal

Provide a user-facing update manager.

## Checklist

- [x] Create update service abstraction.
- [x] Add check for updates.
- [x] Add update list UI.
- [x] Add update details UI.
- [ ] Add download progress UI.
- [ ] Add install progress UI.
- [x] Add reboot-required state.
- [x] Add update history.
- [x] Add failed update recovery message.
- [x] Add settings page for updates.
- [x] Add automatic update setting.
- [x] Add manual update button.
- [x] Add read-only pacman integration.
- [ ] Add custom repo support placeholder.
- [ ] Add signed update notes.
- [ ] Add rollback strategy placeholder.

## Initial Update Backend

Note: 2026-07-10: The first update backend is intentionally read-only. It uses `pacman -Qu` to list installed-package updates, handles non-Arch hosts as an unavailable state, and never synchronizes databases or installs packages.
Note: 2026-07-13: Added persisted daily automatic checks, update-available notifications, a bounded persistent check history, and reboot-required detection. Automatic checks remain read-only and never download or install packages.

First version may wrap:


pacman -Syu


But long term, the system should use safer update handling.

Consider future support for:

- Signed packages
- Custom pacman repository
- Immutable root filesystem
- Snapshot-based rollback
- Btrfs snapshots
- Image-based updates

Do not implement automatic destructive updates until reviewed.

---

# Phase 12: Installer

## Goal

Create an installer to install the OS onto a disk.

## Important Safety Warning

Installer work is dangerous. The installer can destroy data.

Before implementing disk formatting, partitioning, or installation logic:

- Add clear warnings.
- Use VM-only testing first.
- Require explicit confirmation.
- Never run disk operations automatically.
- Never guess which disk to format.
- Show disk model, size, and path clearly.
- Require repeated confirmation before formatting.

## Checklist

- [x] Create installer project placeholder.
- [x] Add installer welcome screen.
- [x] Add language selection placeholder.
- [x] Add keyboard selection placeholder.
- [x] Add timezone selection placeholder.
- [x] Add user creation screen.
- [x] Add disk selection screen.
- [x] Add install summary screen.
- [x] Add confirmation screen.
- [x] Add progress screen.
- [x] Add completion screen.
- [x] Add VM-only warning.
- [x] Add fake/mock install mode.
- [ ] Add real install mode later.
- [x] Add partitioning plan documentation.
- [x] Add bootloader plan documentation.
- [x] Add encryption option placeholder.
- [x] Add recovery partition placeholder.

Note: 2026-07-12: Added the standalone `DaisyOS.Installer` Avalonia wizard with a mock-only backend, fictional disks, VM acknowledgement, validated account details, typed confirmation, simulated progress, and explicit completion messaging. The view model refuses disk-capable backends; no real storage operations are present. See `docs/installer.md`.

## Installer Backend

Future backend tasks:

- [ ] Detect disks.
- [ ] Detect partitions.
- [ ] Create partition table.
- [ ] Format partitions.
- [ ] Mount target filesystem.
- [ ] Install base system.
- [ ] Install packages.
- [ ] Configure users.
- [ ] Configure locale.
- [ ] Configure timezone.
- [ ] Configure hostname.
- [ ] Configure bootloader.
- [ ] Install shell.
- [ ] Enable services.
- [ ] Unmount safely.
- [ ] Reboot.

Do not implement these until the shell and ISO are stable.

---

# Phase 13: Login, Lock Screen, and Users

## Goal

Create user session features.

## Checklist

- [x] Add lock screen UI.
- [x] Add password prompt placeholder.
- [x] Add user avatar placeholder.
- [x] Add lock shortcut (Super+L).
- [x] Add idle lock timer (5 min).
- [x] Add login manager research.
- [x] Add greetd or SDDM integration plan.
- [x] Add user settings page.
- [x] Add change password placeholder.
- [x] Add create user placeholder.
- [x] Add delete user placeholder.
- [x] Add parental/control mode placeholder.
- [x] Add session switching placeholder.

Note: 2026-07-12: Replaced the permanent three-attempt lockout with a tested 30-second cooldown and visible retry countdown. Added Settings placeholders for password changes, account creation/removal, parental controls, and session switching, plus the SDDM/KWin integration and production-lock requirements in `docs/session-management.md`.

## Notes

A real login screen should integrate with an existing login manager such as `greetd`, LightDM, or SDDM.

Do not attempt to replace Linux authentication from scratch.

---

# Phase 14: Window Management and Compositor Integration

## Goal

Make the environment feel more like a real desktop OS by relying on a real compositor/window manager for application windows.

## Important Note

Avalonia alone is not a full Linux window manager.

For real desktop behavior, the system needs:

- X11 window manager, or
- Wayland compositor, or
- existing desktop/compositor integration

DaisyOS should not code the actual third-party application windows itself. Normal apps create their own windows. The compositor/window manager manages those windows.

## Current Direction

Recommended first serious target:


KWin Wayland


Fallback:


Xorg + lightweight window manager


Visual experiment option:


Hyprland


## Checklist

- [x] Use Xorg only as early fallback/prototype.
- [x] Research KWin Wayland session integration.
- [x] Research KWin window decoration theming.
- [x] Research how DaisyOS shell should coexist with KWin.
- [x] Research layer/panel/shell surface options.
- [ ] Research lightweight X11 window managers.
- [ ] Research Wayland compositors.
- [x] Decide short-term windowing strategy.
- [x] Decide long-term compositor strategy.
- [x] Add running app list.
- [x] Add basic app switching UI.
- [ ] Add minimize/restore behavior if supported.
- [ ] Add workspace placeholder.
- [ ] Add window overview placeholder.
- [x] Add Alt+Tab UI placeholder.
- [ ] Add screenshots placeholder.

## Possible Strategies

### Strategy A: Kiosk-like Shell

The Avalonia shell is the main fullscreen UI. Other apps are limited or launched inside controlled flows.

Pros:

- Easier
- More controlled
- Good for appliance-like systems

Cons:

- Not a full desktop OS feel

### Strategy B: Existing Window Manager / Compositor

Use a real compositor/window manager underneath.

Pros:

- Allows normal Linux apps
- Does not require writing a compositor
- Gives real app windows, resizing, snapping, and Alt+Tab

Cons:

- Less complete visual control
- Some third-party apps draw their own decorations

### Strategy C: Custom Wayland Compositor

Build or deeply customize a Wayland compositor.

Pros:

- More control
- Closer to a real OS shell

Cons:

- Much harder
- Large separate project
- Requires handling input, outputs, focus, XWayland, scaling, windows, layer surfaces, security, and more

Recommended path:


Start with Strategy B using KWin Wayland.
Keep Xorg fallback for early ISO testing.
Consider deeper compositor ownership much later.


Note: 2026-07-12: Added a responsive Alt+Tab overlay backed by the Dock's running-app state. Repeated Tab/Shift+Tab advances selection, releasing Alt activates the selection, and internal Settings/Files/Updates windows restore directly. Authoritative switching for externally focused windows still requires compositor integration.
Note: 2026-07-13: Added a read-only compositor-session preflight plus an executable KWin acceptance matrix covering shell surfaces, owned windows, native Wayland and XWayland apps, decorations, blur, scaling, portals, gaming, crash recovery, and Xorg fallback. Repository fixtures verify healthy and broken preflight classification.
Note: 2026-07-14: Added and executed a bounded nested-KWin runtime harness. It binds strict preflight to the exact nested process, proves the unique Wayland socket accepts a native client, launches DaisyOS plus an XWayland client, preserves the parent graphical session, and records zero required failures or warnings. A1, A2, A4, and A5 now have automated evidence; A3 retains its earlier observed visual result. Interactive B-F and disposable-QEMU G gates remain.

---

# Phase 15: Wayland, KWin, and Vibrancy

## Goal

Plan the transition from Xorg prototype to a modern Wayland-based desktop environment with blur/vibrancy support.

## Checklist

- [x] Research Wayland basics.
- [x] Research KWin Wayland integration.
- [X] Vibrancy/blur.
- [x] Research KWin window decoration themes.
- [ ] Research wlroots.
- [ ] Research Smithay.
- [ ] Test shell with GNOME Wayland.
- [~] Test shell with KDE/KWin Wayland (automated nested startup passes; interactive matrix remains).
- [ ] Test shell with Hyprland for visual experiments.
- [~] Test Avalonia on Wayland (shell runs through XWayland; native Wayland client round trip passes).
- [x] Decide whether shell should run as:
  - [x] Desktop layer
  - [x] Panel
  - [x] Launcher
  - [x] Settings/control center
  - [x] Normal window during early development
- [x] Decide compositor ownership.
- [x] Create compositor integration plan.
- [x] Add protocol needs.
- [x] Add window management needs.
- [x] Add lock screen integration needs.
- [x] Add screenshot/screen recording needs.
- [x] Add multi-monitor needs.
- [x] Add fractional scaling needs.
- [x] Add VRR needs.
- [x] Add HDR needs.

Do not start by writing a compositor unless explicitly requested. Build the shell first.

## Backdrop Blur / Vibrancy Direction

DaisyOS wants Apple-like vibrancy or Tailwind-style `backdrop-blur-lg` behavior.

Two levels:


Inside DaisyOS shell:
  Avalonia can fake glass using translucency, shadows, tints, gradients, and internal effects.

Behind normal app windows:
  Requires compositor support.


For real system-level backdrop blur:

- Use KWin blur where possible.
- Use Hyprland only for visual experiments.
- Do not claim real vibrancy until it is tested under the target compositor.

---

# Phase 16: Security and Lockdown

## Goal

Control what normal users can see and access without pretending physical-access security is solved.

## Checklist

- [ ] Create non-root default user.
- [ ] Remove sudo access from normal user in locked-down builds.
- [ ] Restrict terminal access in locked-down builds.
- [ ] Hide or remove unnecessary TTY access in locked-down builds.
- [ ] Disable unwanted services.
- [ ] Create app allowlist.
- [ ] Restrict package manager UI.
- [ ] Add filesystem permission strategy.
- [ ] Add secure update strategy.
- [ ] Add firewall settings.
- [ ] Add privacy settings.
- [ ] Add telemetry policy.
- [ ] Add logs policy.
- [ ] Add crash reporting placeholder.
- [ ] Add parental/control mode placeholder.
- [ ] Add recovery mode plan.
- [ ] Add Secure Boot plan.
- [ ] Add disk encryption plan.

## Physical Access Warning

Complete control is not possible if a technical user has physical access unless the system also uses:

- UEFI password
- Secure Boot
- Locked boot order
- Disk encryption
- Signed updates
- No root shell
- No external boot
- Tamper-resistant hardware settings

Document this clearly.

---

# Phase 17: Branding

## Goal

Create a complete brand identity for the OS.

## Checklist

- [x] Confirm final OS name.
- [x] Add logo.
- [x] Add boot logo placeholder.
- [x] Add wallpaper.
- [x] Add icon style.
- [x] Add system sounds placeholder.
- [x] Add color palette.
- [x] Add typography choices.
- [x] Add app icons.
- [x] Add about screen branding.
- [x] Add ISO filename branding.
- [x] Add hostname default.
- [x] Add package/repo naming.
- [x] Add documentation branding.

Note: 2026-07-13: Finalized the DaisyOS identity and naming rules, added a branded About hero, boot and sound-theme placeholders, live-media issue/MOTD and hostname, tracked ISO startup helpers, and CI validation that rejects legacy product names. See `docs/branding.md`.

Do not use Apple, Microsoft, Google, or other company assets.

---

# Phase 18: Core System Apps

## Goal

Create basic built-in apps that make the OS feel complete.

## Required DaisyOS Apps

These apps should eventually be DaisyOS-branded Avalonia apps:

1. DaisyOS Settings
2. DaisyOS Welcome
3. DaisyOS Files
4. DaisyOS Software / Updates
5. DaisyOS System Monitor
6. DaisyOS Screenshot
7. DaisyOS Game Center
8. DaisyOS Notes
9. DaisyOS Calculator
10. DaisyOS Photos
11. DaisyOS Terminal

## Checklist

- [~] Settings
- [ ] Welcome app
- [~] File Manager
- [~] Software/update center
- [ ] System Monitor
- [ ] Screenshot tool
- [ ] Game Center
- [ ] Text editor / Notes
- [ ] Calculator
- [ ] Image viewer / Photos
- [ ] Terminal placeholder or restricted developer terminal
- [ ] About this device
- [ ] Help app
- [ ] Logs/diagnostics app
- [ ] Network setup
- [ ] Bluetooth setup
- [ ] Display settings
- [X] Wallpaper picker
- [ ] App store placeholder

Note: Settings and File Manager exist today as in-shell views (Phase 6, Phase 8) but are not yet split into standalone DaisyOS-branded apps/processes. This phase's checkboxes track the standalone-app packaging milestone, not the underlying functionality, which is further along than the checkboxes alone suggest.

Each app can start as a view inside the shell and later become a separate process if needed.

# Phase 19: Default Applications

## Goal

Ship a useful desktop-first system after install without making the base system bloated.

## Default Installed Applications

Recommended first default app set:

- Firefox
- File manager fallback until DaisyOS Files is ready
- Terminal fallback until DaisyOS Terminal is ready
- Text editor fallback until DaisyOS Notes is ready
- Image viewer fallback until DaisyOS Photos is ready
- Archive manager
- Calculator fallback until DaisyOS Calculator is ready
- Screenshot tool fallback until DaisyOS Screenshot is ready
- System monitor fallback until DaisyOS System Monitor is ready
- GNOME Disks or equivalent disk utility
- pavucontrol or equivalent audio control
- Blueman or equivalent Bluetooth manager
- DaisyOS Settings
- DaisyOS Shell
- DaisyOS Welcome
- DaisyOS Update placeholder

## Optional Recommended Apps

These should be offered in DaisyOS Welcome or DaisyOS Software, not forced into every install.

### Everyday

- LibreOffice
- Thunderbird
- VLC
- Spotify
- Discord
- Signal
- Telegram

### Creative

- GIMP
- Inkscape
- Krita
- Blender
- Kdenlive
- Audacity
- OBS Studio

### Development

- VS Code
- Git
- Docker or Podman
- Node.js
- Python
- JetBrains Toolbox

### Gaming

- Steam
- Lutris
- Heroic Games Launcher
- Bottles
- MangoHud
- Gamescope
- ProtonUp-Qt

## Default App Philosophy

- Install essentials by default.
- Offer power-user apps after first boot.
- Do not bloat the base system.
- Make optional installs discoverable from Welcome/Software.
- Keep gaming packages in a separate desktop-plus-gaming profile if possible.

---

# Phase 20: First-Run Onboarding

## Goal

Create a polished first-use setup experience.

## Checklist

- [x] Welcome screen.
- [x] Choose language placeholder.
- [x] Choose region placeholder.
- [x] Choose keyboard placeholder.
- [x] Connect to Wi-Fi.
- [x] Choose theme.
- [x] Create user placeholder.
- [x] Privacy options.
- [x] Gaming setup offer.
- [x] Recommended apps page.
- [x] Update check.
- [x] Finish screen.
- [x] Launch desktop after onboarding.
- [x] Store onboarding complete flag.

Store first-run state in:


/var/lib/DaisyOS/firstboot-complete


or user config if appropriate.

Note: 2026-07-12: Onboarding is implemented as a versioned, full-screen Welcome surface. It stores completion and non-secret preferences in the existing per-user JSON settings, supports forced and bypassed development startup flags, and remains reopenable from the launcher. Locale, keyboard, account mutation, diagnostics transmission, app installation, and package updates remain explicit placeholders.

---

# Phase 21: Packaging

## Goal

Package the shell and OS components properly.

## Checklist

- [x] Create Arch package for shell.
- [x] Create PKGBUILD.
- [x] Package assets.
- [x] Package systemd units.
- [x] Package config files.
- [x] Package default settings.
- [x] Add install script if needed.
- [x] Add post-install service enablement plan.
- [x] Add custom pacman repo placeholder.
- [x] Add package signing plan.
- [x] Add versioning strategy.
- [x] Add changelog.
- [x] Add desktop package profile.
- [x] Add desktop-plus-gaming package profile.
- [x] Add minimal package profile.

Initial development may copy files directly into the ISO. Long-term, use packages.

---

# Phase 22: Desktop-First Gaming Support

## Goal

Make DaisyOS a strong **desktop-first Linux gaming OS**.

DaisyOS should not become a console-only or Steam Deck-style system first. The primary experience should remain a normal desktop operating system with a dock, launcher, settings, files, multitasking, and productivity workflows.

Gaming support should feel integrated into the desktop, similar to how a polished desktop OS would support games:


Desktop first
Gaming ready
Controller friendly
Steam/Proton compatible
Performance-aware
Not locked into Big Picture mode


The goal is to make gaming easy without sacrificing the normal desktop experience.

## Design Principles

- DaisyOS is a desktop OS first.
- Gaming features should be available from the desktop shell and Settings app.
- Steam Big Picture or console mode may exist, but it should not be the default boot experience.
- Users should be able to use the system normally for files, browser, apps, development, and settings.
- Gaming tools should be preconfigured where possible.
- Avoid fragile gaming tweaks that reduce system stability.
- Prefer fresh drivers, Vulkan support, Proton tooling, and good defaults.
- Clearly explain that some anti-cheat games may still require Windows.
- Keep gaming features modular so a non-gaming DaisyOS build is possible later.

## Desktop-First Gaming Architecture

The gaming stack should fit into the normal DaisyOS desktop:

DaisyOS Desktop Shell
  ↓
Gaming Settings Page
  ↓
Steam / Lutris / Heroic / Bottles
  ↓
Proton / Wine
  ↓
DXVK / vkd3d-proton
  ↓
Vulkan
  ↓
Linux GPU drivers

Gaming should be exposed through:

- App launcher
- Dock pinned apps
- Settings → Gaming
- Quick Settings gaming toggle
- Optional Game Mode
- Optional controller-friendly gaming dashboard
- Optional Steam Big Picture launch button

## Gaming UX Requirements

### Desktop Behavior

- [ ] Steam should launch like a normal desktop app.
- [ ] Lutris should launch like a normal desktop app.
- [ ] Heroic Games Launcher should launch like a normal desktop app.
- [ ] Games should appear in the app launcher where possible.
- [ ] Users should be able to pin Steam, Lutris, Heroic, and games to the dock.
- [ ] Users should be able to switch between games and normal desktop apps.
- [ ] Gaming should not hide access to normal desktop features.
- [ ] Gaming should not force Big Picture mode by default.
- [ ] Fullscreen games should not break the shell after closing.
- [ ] The shell should recover gracefully if a game crashes.

### Gaming Mode

Gaming Mode should be an optional desktop mode, not a separate default OS identity.

- [x] Add optional Gaming Mode toggle.
- [x] Add Gaming Mode button to Quick Settings.
- [x] Add Gaming Mode page in Settings.
- [ ] Gaming Mode should enable performance-focused defaults.
- [ ] Gaming Mode should optionally reduce shell animations/effects while gaming.
- [x] Gaming Mode should optionally suppress non-critical notifications.
- [ ] Gaming Mode should optionally enable MangoHud.
- [ ] Gaming Mode should optionally use GameMode.
- [ ] Gaming Mode should optionally launch Steam Big Picture.
- [x] Gaming Mode should be easy to exit back to normal desktop.

## Required Gaming Packages

Create a gaming package group/profile for DaisyOS.

Initial packages:


steam
gamemode
lib32-gamemode
gamescope
mangohud
lib32-mangohud
wine
wine-mono
wine-gecko
winetricks
lutris
heroic-games-launcher
vulkan-tools
mesa-utils
game-devices-udev
bluez
bluez-utils
pipewire
pipewire-pulse
wireplumber


Optional later packages:


bottles
protonup-qt
protontricks
goverlay
vkbasalt
obs-studio
discord


Checklist:

- [x] Add gaming package group documentation.
- [x] Add gaming packages to optional OS profile.
- [x] Add Steam.
- [x] Add GameMode.
- [x] Add Gamescope.
- [x] Add MangoHud.
- [x] Add Wine.
- [x] Add Winetricks.
- [x] Add Lutris.
- [ ] Add Heroic Games Launcher.
- [x] Add Vulkan tools.
- [ ] Add controller udev rules.
- [ ] Add Bluetooth tools.
- [x] Add PipeWire audio stack.
- [ ] Document optional packages.
- [x] Keep gaming packages separate from minimal desktop packages where possible.

## Multilib Support

Steam and many Windows games require 32-bit libraries.

Checklist:

- [ ] Enable Arch `multilib` repository in gaming builds.
- [x] Document why multilib is required.
- [ ] Add 32-bit Mesa libraries for AMD/Intel gaming builds.
- [ ] Add 32-bit NVIDIA libraries for NVIDIA gaming builds.
- [ ] Add 32-bit GameMode.
- [ ] Add 32-bit MangoHud.
- [ ] Add validation script to check multilib availability.
- [ ] Add troubleshooting documentation for missing 32-bit libraries.

Do not assume a gaming build is ready until 32-bit Vulkan support is verified.

## GPU Driver Support

DaisyOS should support AMD, Intel, and NVIDIA GPUs.

### AMD

Recommended packages:

mesa
lib32-mesa
vulkan-radeon
lib32-vulkan-radeon

Checklist:

- [ ] Add AMD Vulkan package plan.
- [ ] Add AMD 32-bit Vulkan package plan.
- [ ] Add AMD GPU detection.
- [ ] Add AMD driver status in Settings → Gaming.
- [ ] Add AMD troubleshooting notes.

### Intel

Recommended packages:

mesa
lib32-mesa
vulkan-intel
lib32-vulkan-intel

Checklist:

- [ ] Add Intel Vulkan package plan.
- [ ] Add Intel 32-bit Vulkan package plan.
- [ ] Add Intel GPU detection.
- [ ] Add Intel driver status in Settings → Gaming.
- [ ] Add Intel troubleshooting notes.

### NVIDIA

Recommended packages:

nvidia
nvidia-utils
lib32-nvidia-utils
nvidia-settings

Checklist:

- [ ] Add NVIDIA package plan.
- [ ] Add NVIDIA 32-bit package plan.
- [ ] Add NVIDIA GPU detection.
- [ ] Add NVIDIA driver status in Settings → Gaming.
- [ ] Add NVIDIA Wayland notes.
- [ ] Add NVIDIA troubleshooting notes.
- [ ] Add fallback plan for nouveau or unsupported GPUs.

### GPU Detection

- [x] Add GPU detection service.
- [x] Detect GPU vendor.
- [x] Detect active Vulkan driver.
- [ ] Detect missing 32-bit Vulkan support.
- [x] Show GPU status in Settings → Gaming.
- [ ] Show warnings for missing drivers.
- [ ] Show command output from `vulkaninfo` in diagnostics if available.

## Vulkan Validation

DaisyOS should include a simple Vulkan validation flow.

Checklist:

- [ ] Add `vulkan-tools`.
- [x] Add system service/helper to run Vulkan checks.
- [x] Check whether `vulkaninfo` runs successfully.
- [x] Check 64-bit Vulkan support.
- [ ] Check 32-bit Vulkan support if possible.
- [x] Show Vulkan status in Settings → Gaming.
- [ ] Warn if Vulkan is missing.
- [ ] Warn if 32-bit Vulkan is missing.
- [ ] Add troubleshooting documentation.

Possible diagnostics:

vulkaninfo
vulkaninfo --summary
glxinfo
lspci -k

Do not expose raw technical output as the default UI. Put raw output behind an advanced diagnostics section.

## Steam and Proton

Steam should be the main default gaming platform.

Checklist:

- [ ] Add Steam package.
- [ ] Add Steam launcher entry.
- [ ] Add Steam dock pin option.
- [x] Add Steam status card in Settings → Gaming.
- [ ] Add Steam Play guidance.
- [ ] Add Proton explanation.
- [ ] Add Proton Experimental guidance.
- [ ] Add Proton-GE installation plan.
- [ ] Add Protontricks or ProtonUp-Qt as optional tools.
- [ ] Add Steam library folder notes.
- [ ] Add shader cache notes.
- [ ] Add Big Picture launch option.
- [x] Add compatibility warning for anti-cheat games.

Steam should open as a normal desktop app by default.

Optional actions:

- Open Steam
- Open Steam Big Picture
- Open Steam settings
- Open Proton compatibility help

## Wine and Non-Steam Games

DaisyOS should support non-Steam gaming through user-friendly tools.

Checklist:

- [ ] Add Wine.
- [ ] Add wine-mono.
- [ ] Add wine-gecko.
- [ ] Add winetricks.
- [ ] Add Lutris.
- [ ] Add Heroic Games Launcher.
- [ ] Add Bottles as optional.
- [ ] Add non-Steam gaming documentation.
- [ ] Add Epic/GOG guidance through Heroic.
- [ ] Add Battle.net/standalone launcher guidance through Lutris/Bottles.
- [ ] Add Wine prefix explanation.
- [ ] Add warning that not all Windows launchers work perfectly.

Do not make custom Wine prefix management in the DaisyOS shell until the basic desktop gaming workflow is stable.

## GameMode Integration

GameMode should be available for performance optimization.

Checklist:

- [ ] Install `gamemode`.
- [ ] Install `lib32-gamemode`.
- [x] Add GameMode status detection.
- [ ] Add GameMode toggle in Settings → Gaming.
- [ ] Add GameMode toggle in Quick Settings.
- [ ] Add explanation of `gamemoderun`.
- [ ] Add Steam launch option helper text.
- [ ] Add per-game GameMode placeholder.
- [ ] Add global GameMode-on-while-gaming placeholder.

Steam launch option example:

gamemoderun %command%

DaisyOS should not force GameMode globally without user control.

## Gamescope Integration

Gamescope should be optional and desktop-friendly.

Use Gamescope for:

- Fullscreen isolation
- Resolution control
- Upscaling
- Frame limiting
- Steam Deck-like game sessions
- Possible HDR-related workflows later

Checklist:

- [ ] Install `gamescope`.
- [x] Add Gamescope status detection.
- [ ] Add Gamescope explanation in Settings → Gaming.
- [ ] Add Gamescope launch option helper.
- [ ] Add per-game Gamescope profile placeholder.
- [ ] Add fullscreen wrapper option.
- [ ] Add resolution selector placeholder.
- [ ] Add frame limiter placeholder.
- [ ] Add FSR/upscaling placeholder.
- [ ] Add HDR placeholder.
- [ ] Add troubleshooting docs.

Example Steam launch option:

gamescope -f -- %command%

Combined example:

gamemoderun gamescope -f -- %command%


Do not make Gamescope mandatory for all games.

## MangoHud Integration

MangoHud should be available as an optional performance overlay.

Checklist:

- [ ] Install `mangohud`.
- [ ] Install `lib32-mangohud`.
- [x] Add MangoHud status detection.
- [ ] Add MangoHud toggle in Settings → Gaming.
- [ ] Add MangoHud overlay presets.
- [ ] Add FPS-only preset.
- [ ] Add full diagnostics preset.
- [ ] Add per-game MangoHud placeholder.
- [ ] Add config file location documentation.
- [ ] Add Steam launch option helper.

Steam launch option:

mangohud %command%

Combined example:

gamemoderun mangohud %command%


## Controller Support

DaisyOS should make controllers easy to use from the desktop.

Checklist:

- [ ] Install `game-devices-udev`.
- [ ] Install `bluez`.
- [ ] Install `bluez-utils`.
- [ ] Add Bluetooth service enablement.
- [ ] Add controller status in Settings → Gaming.
- [ ] Add controller pairing shortcut.
- [ ] Add controller test UI placeholder.
- [ ] Add battery display placeholder.
- [ ] Add Steam Input guidance.
- [ ] Add Xbox controller notes.
- [ ] Add PlayStation controller notes.
- [ ] Add generic controller notes.
- [ ] Add optional `xpadneo` research.
- [ ] Add optional `xone` research.
- [ ] Add optional `dualsensectl` research.

Controller support should be accessible without leaving the desktop shell.

## Audio for Gaming

PipeWire should be the default audio stack.

Checklist:

- [x] Use PipeWire.
- [x] Use WirePlumber.
- [ ] Support PulseAudio compatibility through `pipewire-pulse`.
- [ ] Add volume device switching UI.
- [ ] Add microphone input switching UI.
- [ ] Add Bluetooth headset support.
- [ ] Add low-latency audio notes.
- [ ] Add troubleshooting documentation for no audio in games.
- [ ] Add per-app volume placeholder.

## Display Features for Gaming

DaisyOS should eventually support modern gaming display features.

Checklist:

- [ ] Add refresh rate display setting.
- [ ] Add resolution display setting.
- [ ] Add multi-monitor support plan.
- [ ] Add VRR support plan.
- [ ] Add HDR support plan.
- [ ] Add Gamescope HDR notes.
- [ ] Add Wayland compositor requirement notes.
- [ ] Add per-monitor settings model.
- [ ] Add display diagnostics.

### VRR

- [ ] Detect whether monitor supports VRR.
- [ ] Detect whether compositor supports VRR.
- [ ] Add VRR toggle if supported.
- [ ] Add explanation if unsupported.

### HDR

- [ ] Research compositor HDR support.
- [ ] Add HDR placeholder in Settings → Display/Gaming.
- [ ] Add explanation that HDR depends on compositor and GPU support.
- [ ] Do not claim full HDR support until verified in real games.

## Wayland and Gaming

DaisyOS should move toward Wayland for modern gaming features, but desktop stability comes first.

Checklist:

- [ ] Keep current desktop shell working.
- [ ] Test DaisyOS shell under GNOME Wayland.
- [ ] Test DaisyOS shell under KDE/KWin Wayland.
- [ ] Test DaisyOS shell under Hyprland.
- [ ] Test Steam under Wayland session.
- [ ] Test Gamescope under Wayland session.
- [ ] Test MangoHud under Wayland session.
- [ ] Test fullscreen games under Wayland.
- [ ] Document X11 fallback.
- [ ] Document known NVIDIA Wayland issues if found.

Do not block desktop shell development on perfect Wayland gaming support.

## Gaming Settings Page

Add a new page:

Settings → Gaming

Sections:

Overview
Steam & Proton
Performance
Overlays
Gamescope
Controllers
Drivers & Vulkan
Compatibility
Advanced

Checklist:

- [x] Create Gaming settings page.
- [x] Add gaming overview cards.
- [x] Add Steam installed/running status.
- [x] Add Proton guidance.
- [x] Add GameMode status.
- [x] Add MangoHud status.
- [x] Add Gamescope status.
- [x] Add Vulkan status.
- [x] Add GPU driver status.
- [x] Add controller status.
- [x] Add anti-cheat compatibility warning.
- [x] Add launch option examples.
- [ ] Add troubleshooting links.
- [x] Add advanced diagnostics section.

Example UI cards:

Steam
Installed: Yes/No
Action: Open Steam

Vulkan
Status: Working / Missing / Unknown
Action: Run diagnostics

GameMode
Status: Installed / Missing
Toggle: Enable for supported games

MangoHud
Status: Installed / Missing
Toggle: Show overlay

Controllers
Connected: 0
Action: Pair controller


## Gaming Quick Settings

Add gaming controls to Quick Settings.

Checklist:

- [x] Add Gaming Mode quick toggle.
- [ ] Add Performance Mode quick toggle.
- [ ] Add MangoHud quick toggle.
- [x] Add Do Not Disturb While Gaming toggle.
- [ ] Add Controller Settings shortcut.
- [ ] Add Steam shortcut.
- [ ] Add Game Library shortcut placeholder.

Quick Settings should not become cluttered. Put advanced options in Settings → Gaming.

## Game Library UI

DaisyOS may eventually include a desktop-first game library.

This should not replace Steam, Lutris, or Heroic at first. It should unify shortcuts later.

Checklist:

- [ ] Add Game Library concept document.
- [ ] Add placeholder Game Library view.
- [ ] Detect installed Steam games placeholder.
- [ ] Detect Lutris games placeholder.
- [ ] Detect Heroic games placeholder.
- [ ] Show game artwork placeholder.
- [ ] Launch game placeholder.
- [ ] Pin game to dock placeholder.
- [ ] Add per-game settings placeholder.
- [ ] Add per-game launch options placeholder.
- [ ] Add per-game MangoHud/GameMode/Gamescope placeholder.

Do not build a full game library until Steam/Lutris/Heroic desktop workflows are stable.

## Anti-Cheat and Compatibility

DaisyOS must be honest about compatibility.

Checklist:

- [x] Add compatibility warning to Settings → Gaming.
- [ ] Add ProtonDB link/documentation placeholder.
- [ ] Add explanation that some anti-cheat games may not work.
- [ ] Add explanation that support depends on game developers.
- [ ] Add “Windows may still be required for some games” note.
- [ ] Add troubleshooting docs for broken games.
- [ ] Avoid promising universal Windows game support.

Suggested wording:

Many Windows games work through Proton or Wine, especially single-player games and multiplayer games with Linux-compatible anti-cheat. Some competitive games may not work because their anti-cheat systems do not support Linux or Proton.

## Gaming Diagnostics

Create a diagnostics flow for gaming support.

Checklist:

- [x] Add Gaming Diagnostics page.
- [x] Check GPU model.
- [x] Check active driver.
- [x] Check Vulkan support.
- [x] Check 32-bit Vulkan support.
- [x] Check Steam installation.
- [x] Check GameMode installation.
- [x] Check MangoHud installation.
- [x] Check Gamescope installation.
- [x] Check Wine installation.
- [x] Check controller rules.
- [x] Check Bluetooth service.
- [x] Show user-friendly status.
- [x] Hide raw logs behind advanced details.
- [x] Add copy diagnostics button.

Possible commands:

lspci -k
vulkaninfo --summary
glxinfo -B
systemctl status bluetooth
which steam
which gamemoderun
which mangohud
which gamescope
wine --version

All commands must be run safely through the existing command runner service.

## Performance Profiles

Add desktop-friendly performance profiles.

Profiles:

Balanced
Performance
Quiet
Battery Saver


Checklist:

- [ ] Add performance profile model.
- [ ] Add profile selector in Settings → Gaming.
- [ ] Add profile selector in Quick Settings.
- [ ] Add mock implementation first.
- [ ] Research power-profiles-daemon.
- [ ] Research auto-cpufreq.
- [ ] Research tuned.
- [ ] Add safe backend later.
- [ ] Do not apply risky kernel/sysctl tweaks by default.

Performance Mode should be conservative and reversible.

## Notifications While Gaming

Gaming should reduce distractions without hiding important system alerts.

Checklist:

- [x] Add “Do Not Disturb while gaming” setting.
- [ ] Suppress non-critical notifications during fullscreen games.
- [x] Allow critical battery/thermal/system notifications.
- [ ] Show missed notifications after game exits.
- [ ] Add per-app notification rules placeholder.
- [ ] Add game detection placeholder.

## Packaging Profiles

Create separate OS package profiles.

Profiles:


DaisyOS Desktop
DaisyOS Desktop + Gaming
DaisyOS Minimal

Checklist:

- [x] Add package profile concept.
- [x] Add desktop package list.
- [x] Add gaming package list.
- [x] Keep gaming packages optional initially.
- [ ] Add ISO profile placeholder for gaming build.
- [x] Document package differences.
- [x] Avoid forcing gaming tools on minimal builds.

## Implementation Order

### Milestone G1: Desktop Gaming Foundation

- [x] Add Gaming phase to ROADMAP.md.
- [x] Add gaming package list documentation.
- [x] Add multilib requirement documentation.
- [x] Add Gaming settings page placeholder.
- [x] Add GPU detection with live Vulkan/GameMode/MangoHud/Gamescope status.
- [ ] Add Steam launcher shortcut placeholder.
- [x] Add compatibility warning.

### Milestone G2: Gaming Tools

- [x] Add Steam package to gaming profile.
- [x] Add GameMode package.
- [x] Add MangoHud package.
- [x] Add Gamescope package.
- [x] Add Vulkan tools.
- [x] Add Wine/Lutris/Heroic package plan.
- [x] Add controller package plan.

### Milestone G3: Gaming Settings Integration

- [x] Detect Steam.
- [x] Detect Vulkan.
- [x] Detect GPU vendor.
- [x] Detect GameMode.
- [x] Detect MangoHud.
- [x] Detect Gamescope.
- [x] Show status cards in Settings → Gaming.
- [x] Add launch option helper text.

### Milestone G4: Desktop Gaming UX

- [ ] Add Steam to launcher.
- [ ] Add Steam dock pin.
- [x] Add optional Gaming Mode toggle.
- [x] Add Do Not Disturb while gaming.
- [ ] Add controller pairing shortcut.
- [ ] Add Game Library placeholder.

### Milestone G5: Advanced Gaming

- [ ] Add Gamescope profiles.
- [ ] Add MangoHud presets.
- [ ] Add per-game settings placeholder.
- [ ] Add VRR support plan.
- [ ] Add HDR support plan.
- [x] Add Wayland gaming test matrix.
- [ ] Add optional Steam Big Picture mode.

## Definition of Done for Gaming Support

A gaming task is done only when:

- The desktop shell still works normally.
- The feature does not force console-style usage.
- The feature is accessible through Settings or the launcher.
- The feature has a mock state if real system integration is not ready.
- Missing packages are handled gracefully.
- User-facing errors are understandable.
- No risky system tweaks are applied silently.
- Documentation is updated.
- ROADMAP.md is updated.
- README.md is updated if user-facing behavior changed.
- The project builds successfully.
- Existing tests still pass.

## Do Not Do Yet

Do not implement these until explicitly approved:

- Kernel-level gaming tweaks.
- Automatic overclocking.
- GPU undervolting.
- Risky sysctl tweaks.
- Forced custom kernels.
- Forced Gamescope for all games.
- Forced Steam Big Picture on boot.
- Disabling desktop features for gaming.
- Replacing the desktop shell with a console UI.
- Claiming all Windows games work.
- Claiming all anti-cheat games work.
- Enabling HDR by default before real testing.
- Disk partitioning or installer changes for gaming mode.

---

# Phase 23: Testing

## Goal

Make the project testable and reliable.

## Checklist

- [x] Add unit tests for services.
- [x] Add ViewModel tests.
- [x] Add command runner tests.
- [x] Add settings persistence tests.
- [x] Add desktop command parser tests.
- [x] Add file manager navigation/filtering tests.
- [x] Add update service tests in mock/fake-runner mode.
- [~] Add shell startup test checklist.
- [~] Add ISO boot test checklist.
- [~] Add VM smoke test checklist.
- [x] Add manual QA checklist.
- [x] Add release checklist.
- [x] Add CI validation and release workflows.
- [x] Add linting/formatting checks.
- [x] Add gaming diagnostics tests.
- [x] Add compositor session test checklist.

## Notes

- 2026-07-02: Test scripts and manual shell smoke checklist exist.
- 2026-07-10: Added the `DaisyOS.Tests` xUnit project with nine tests covering safe command execution, literal arguments, output/error capture, missing commands, failures, timeouts, JSON settings defaults, persistence, normalization, and change events. Service coverage remains in progress for the other Linux integrations.
- 2026-07-13: Added five Welcome ViewModel tests covering automatic Wi-Fi/update step entry, mandatory versus reopenable exit rules, completion persistence, and calm service-failure states. Added fixture-driven compositor preflight tests and the manual nested-KWin/QEMU session matrix.

## Manual Shell Smoke Test

After running the shell locally, verify:

- [ ] Shell launches.
- [ ] UI is fullscreen.
- [ ] `Ctrl+Q` quits during development.
- [ ] `Escape` closes overlays.
- [ ] Clock updates.
- [ ] Launcher opens.
- [ ] Settings opens.
- [ ] Power menu opens.
- [ ] Volume menu opens.
- [ ] Theme/volume settings persist.
- [ ] Missing Linux commands do not crash the shell.

## Manual ISO Smoke Test

After building ISO, verify:

- [x] ISO boots in QEMU.
- [x] No kernel panic.
- [x] Auto-login works.
- [x] Xorg or target compositor starts.
- [x] Avalonia shell starts.
- [x] UI is fullscreen.
- [x] Clock updates.
- [x] Launcher opens.
- [x] Settings opens.
- [x] Power menu opens.
- [x] Reboot action works in VM.
- [x] Shutdown action works in VM.
- [x] Shell restarts after crash.
- [x] NetworkManager is running.
- [x] PipeWire is running.
- [x] Logs are available.

---

# Phase 24: Documentation

## Goal

Document the system so humans and AI agents can continue development safely.

## Checklist

- [x] Write `README.md`.
- [x] Write `docs/architecture.md`.
- [x] Write `docs/shell.md`.
- [x] Write `docs/iso-build.md`.
- [x] Write `docs/testing.md`.
- [x] Write `docs/troubleshooting.md`.
- [x] Write `docs/security.md`.
- [x] Write `docs/installer.md`.
- [x] Write `docs/update-system.md`.
- [x] Write `docs/theming.md`.
- [x] Write `docs/packaging.md`.
- [x] Write `docs/ai-agent-guidelines.md`.
- [x] Write `docs/gaming.md`.
- [x] Write `docs/compositor.md`.
- [x] Write `docs/compositor-test-matrix.md`.
- [x] Write `docs/default-apps.md`.
- [x] Write `docs/window-controls.md`.
- [x] Write `docs/release.md` and release notes template.

## AI Agent Guidelines

Add clear instructions:

- Always update `ROADMAP.md` when finishing tasks.
- Mark completed tasks with `[x]`.
- Mark active tasks with `[-]`.
- Mark blocked tasks with `[!]`.
- Add notes when behavior is uncertain.
- Do not implement destructive disk logic without explicit approval.
- Do not remove safety checks.
- Do not hardcode private user paths.
- Prefer scripts with clear variables.
- Keep shell scripts safe with `set -euo pipefail` where appropriate.
- Avoid unnecessary dependencies.
- Keep UI code organized using MVVM.
- Keep system command wrappers separate from UI code.
- Prefer mock services for development.
- Do not start ISO generation unless explicitly requested.
- Do not start installer/partitioning/formatting work unless explicitly requested.
- Do not modify the host system outside this repository.
- Do not run `sudo pacman -Syu` automatically on the host.
- Before marking any Phase checklist item `[x]`, verify it against the Notes section in that same phase and against `Milestone` sections below — if they disagree, reconcile the checkboxes rather than leaving the mismatch for the next session.

---

# Phase 25: Release Pipeline

## Goal

Create repeatable builds and releases.

## Checklist

- [x] Add version number.
- [x] Add build metadata.
- [x] Add changelog.
- [x] Add release notes template.
- [x] Add ISO output naming convention.
- [x] Add checksum generation.
- [x] Add signature placeholder and clearly mark signing as future work.
- [x] Add release build script.
- [x] Add debug build script.
- [x] Add clean release process.
- [x] Add artifact storage plan.
- [x] Add update channel plan:
  - [x] Stable
  - [x] Beta
  - [x] Nightly

## ISO Naming Convention

Use:


DaisyOS-YYYY.MM.DD-x86_64.iso


or:


DaisyOS-VERSION-x86_64.iso


---

# Phase 26: Long-Term Advanced Features

## Goal

Track future ideas without blocking the MVP.

## Checklist

- [ ] KWin Wayland integration.
- [ ] Wayland compositor integration.
- [ ] Custom compositor research.
- [ ] Custom login manager.
- [ ] Custom lock screen integrated with real auth.
- [ ] App sandboxing.
- [ ] Flatpak support.
- [ ] Custom app store.
- [ ] Immutable root filesystem.
- [ ] Btrfs snapshots.
- [ ] Rollback UI.
- [ ] Secure Boot.
- [ ] Disk encryption.
- [ ] Recovery partition.
- [ ] Cloud account integration.
- [ ] Sync settings.
- [ ] Touch/tablet mode.
- [ ] Accessibility improvements.
- [ ] Screen reader support.
- [ ] Voice input placeholder.
- [ ] Multi-monitor management.
- [ ] Fractional scaling.
- [ ] GPU settings.
- [ ] Game mode.
- [ ] Developer mode.
- [ ] Remote support.
- [ ] Enterprise/device management mode.

---

# Current Recommended Build Order

Follow this order unless instructed otherwise.

## Milestone 1: Working Shell App

- [x] Create Avalonia shell.
- [x] Make it fullscreen.
- [x] Add dock/taskbar.
- [x] Add launcher.
- [x] Add settings placeholder.
- [x] Add power menu.
- [x] Add theme.

## Milestone 2: Publishable Shell

- [x] Publish self-contained Linux binary.
- [x] Add publish script.
- [x] Smoke test instructions in publish script.

## Milestone 3: App Launcher and Settings Foundation

- [x] Add real app registry that loads installed `.desktop` files.
- [x] Add launcher search over the app registry.
- [x] Add pinned-app support backed by persisted settings.
- [x] Add running-app indicators once app launching exists.
- [x] Start shaping Settings into real pages for Appearance, Sound, Network, About, and Gaming.

## Milestone 4: Real System Features

- [x] Power actions.
- [x] Network status.
- [x] Wi-Fi UI with connect/disconnect.
- [x] Audio status.
- [x] Battery status.
- [x] Bluetooth status.
- [x] Settings persistence.
- [x] User-friendly error handling.

## Milestone 5: OS-Like Experience

- [x] File manager.
- [x] Notification center.
- [x] App launcher.
- [x] App allowlist/report-only policy placeholder.
- [x] Read-only update manager.
- [x] Onboarding.
- [x] Default apps plan.
- [x] DaisyOS Welcome.

## Milestone 6: Compositor/Desktop Integration

- [~] Research and test KWin Wayland (repository, fixture, automated nested isolation, observed A3, and strict preflight gates pass; interactive/QEMU execution remains).
- [~] Test DaisyOS shell under KWin Wayland (published shell and XWayland client run in isolated nested KWin; interactive matrix, native shell transport, and QEMU evidence remain).
- [x] Add window/decorations strategy.
- [x] Add blur/vibrancy strategy.
- [x] Add third-party app window behavior notes.
- [x] Add fallback Xorg path.

## Milestone 7: Bootable ISO

- [x] Create or validate archiso profile.
- [x] Add packages.
- [x] Copy shell into ISO.
- [x] Add systemd service.
- [x] Build ISO.
- [x] Boot ISO in QEMU.
- [x] Confirm shell starts.
- [x] Confirm fallback/recovery path.
- [x] Bind the staged production shell and embedded ISO payload to the current source fingerprint.
- [x] Pass the authoritative BIOS, UEFI, runtime-service, stability, Restart, and Shut down alpha gate on one fresh image (`DaisyOS-2026.07.16-x86_64.iso`; accepted 2026-07-16).

## Milestone 8: Desktop-First Gaming

- [x] Add Gaming settings placeholder.
- [x] Add package profile plan.
- [x] Add Steam/Proton guidance.
- [x] Add Vulkan diagnostics placeholder.
- [x] Add GameMode/MangoHud/Gamescope status cards.
- [x] Add controller support plan.

## Milestone 9: Installable System

- [ ] Installer mockup.
- [ ] Installer backend plan.
- [ ] VM-only installer.
- [ ] Real install process after review.

---

# Definition of Done

A task is done only when:

- Code builds.
- App runs or script executes successfully.
- Errors are handled.
- Documentation is updated.
- `ROADMAP.md` checklist is updated.
- Any new commands are documented.
- Unsafe behavior is clearly guarded.
- The change has been tested at least manually.

For UI tasks:

- View exists.
- ViewModel exists where appropriate.
- Mock data exists if real data is not ready.
- UI handles loading, empty, and error states when relevant.
- Styling matches the design system.

For system tasks:

- Commands are wrapped safely.
- Errors are captured.
- Missing dependencies are handled.
- Behavior is documented.
- No destructive action occurs without confirmation.

For ISO tasks:

- ISO builds.
- ISO boots in VM.
- Logs can be inspected.
- Shell starts automatically.
- Failure recovery is documented.

For gaming tasks:

- Desktop shell remains the primary experience.
- Feature is accessible through Settings, launcher, or Quick Settings.
- Missing packages are handled gracefully.
- No risky system tweaks are applied silently.
- Compatibility limitations are communicated honestly.

---

# Known Risks

## Technical Risks

- Avalonia graphics behavior may differ between desktop Linux, Xorg, Wayland, KWin, Hyprland, and framebuffer.
- Xorg startup from systemd may need iteration.
- KWin session integration may need research and testing.
- Some features require root permissions or polkit integration.
- Real window management requires a compositor or window manager.
- Installer logic is dangerous and must be handled carefully.
- Hardware support can vary widely.
- Secure Boot and encryption add complexity.
- Updates can break Arch-based systems if not controlled.
- Gaming features depend heavily on GPU drivers, Vulkan, 32-bit libraries, Proton compatibility, and game-specific anti-cheat behavior.
- Automated coverage now protects core services, settings, networking, updates, app policy, file management, idle locking, startup behavior, and rendering calculations. UI interaction and real compositor/ISO coverage still rely on manual and VM smoke tests.

## Product Risks

- Building a complete OS-like experience is a large project.
- Visual polish takes time.
- User expectations are high because people compare operating systems to Windows and macOS.
- App ecosystem decisions must be made early.
- Security and recovery are essential for real-world use.
- Gaming users expect very good defaults and honest compatibility messaging.

---

# Do Not Do Yet

The AI agent should not implement these until explicitly requested:

- Real disk formatting.
- Real installer partitioning.
- Removing user recovery access.
- Disabling all TTYs permanently.
- Secure Boot changes.
- Bootloader locking.
- Encryption setup.
- Deleting existing system files.
- Running `pacman -Syu` automatically on the host machine.
- Modifying the host system outside this repository.
- Assuming the development machine is the target OS.
- Copying proprietary Apple, Microsoft, or Google UI assets.
- Writing a custom compositor.
- Forcing Steam Big Picture on boot.
- Forcing Gamescope for all games.
- Kernel-level gaming tweaks.
- Automatic overclocking.
- GPU undervolting.

---

# Immediate Next Task for AI Agent

## Remove app performance overlay — 2026-10-04

- [x] Delete AppPerformanceOverlay markup/code-behind, its Files host and toggle method, and the unused Console Settings performance-overlay row, event, handler, and navigation entry.
- [x] Confirm no remaining runtime/build references to the removed overlay. Retain AppPerformanceMonitor because ShellMetricsMonitor actively uses it for separate diagnostics.
- [x] Build, pass 34 headless tests and 343 main tests (one opt-in screenshot test skipped), and reload the native shell. Open Files and verify the overlay is absent in the live capture (`artifacts/files-startup-2026-10-04/without-performance-overlay.png`). Reconcile the outdated Quick Settings test with the previously requested neutral icon styling.

## Files startup decorations — 2026-10-04

- [x] Guard the Avalonia Wayland initial decoration configure with the existing client-side/no-decoration request, matching subsequent configure handling. Preserve the pre-existing fork edits.
- [x] Correct the earlier incomplete fix: native surface creation precedes Files' XAML decoration setting. Add a synchronous, one-window client-decoration creation scope and use it for Files so its first Wayland negotiation never requests server decorations. Ordinary application windows retain their current decoration path.
- [x] Rebuild/reload and pass all 34 headless tests. Fresh Files opening reports `noBorder=true` at window addition; sampled startup captures show only Files' own frame (`initial-csd-*.png`). The previous guard alone was rejected by the user's reproduced flash.
- [x] Build and reload the native shell; pass four focused Files/Wayland tests and both working-tree diff checks.
- [x] Open Files in the running session: KWin reports `caption=Files noBorder=true` when added, and the captured startup sequence contains no extra titlebar once Files appears (`artifacts/files-startup-2026-10-04`). Captures are sampled, not a recording of every presented frame.

## Overview filtering and Quick Settings colors — 2026-10-04

- [x] Revert the source-resolution/mipmap preview pass after user-reported excessive blur; restore direct KWin thumbnail sampling and remove the extra texture component/test. Preserve layout and the smooth rounded mask.
- [x] Deploy the rollback as overview v14 to bypass QML caching. Build, 29 Qt tests, package/install checks, and diff checks pass; inspect the restored direct-sampling previews in the running overview (`artifacts/preview-quality-2026-10-04/restored-direct.png`).
- [x] Keep Quick Settings labels at their default color and weight when active; use System Bar `ContentSecondaryBrush` for icons and labels, and explicitly neutral tooltip text. Update global active-content styles as well as the tile component.
- [x] Pass 32 Qt tests including a rendered fine-pattern downscaling test, 34 headless tests including Light/Dark foreground and label invariance, 343 main tests (one opt-in screenshot test skipped), package checks, and diff/shell checks. The fine-pattern test did not predict real application text quality; do not treat it as visual acceptance.
- [x] Initially deploy overview v13 and inspect live Chrome, VS Code, and Files previews (`artifacts/preview-quality-2026-10-04/after.png`). Quick Settings color/tooltip behavior was verified in headless Light/Dark runs; live pointer/tooltip acceptance remains unverified.

## Quick Settings component — 2026-10-04

- [x] Extract Quick Settings markup, local styles, pages, service calls, and handlers into `Views/Components/QuickSettings/QuickSettings.axaml` and `.axaml.cs`; keep the System Bar responsible for its clock/status icons and flyout coordination.
- [x] Preserve native flyout placement and point KWin blur at the component's material frame; retain the component reference for detached overlay operation.
- [x] Build and pass 343 main tests (one opt-in capture test skipped) plus 33 headless tests, including detached component controls and console-action forwarding. Reload the running shell and confirm service startup; live popup interaction/appearance remains unverified because injected touch input did not open the flyout.
- [x] Identify screenshot tool UI in `Views/Components/Screenshot/ScreenshotOverlay.axaml` and `.axaml.cs`; its host is `Views/ShellView.axaml`, and capture integration is `Services/Screenshot/DaisyScreenshotService.cs`.

## Login background — 2026-10-04

- [x] Remove the fullscreen rounded KWin blur region; keep the wallpaper sharp and the display corners square.
- [x] Retain a separate sharp wallpaper surface so KWin can blur only the password and avatar material containers on the foreground surface. Track their geometry through the existing KWinBlur implementation.
- [x] Restore container blur in the native session; build and six focused geometry/lock tests pass. Inspect `artifacts/login-2026-10-04/login-container-blur.png` to verify both containers are blurred while the wallpaper remains sharp and the screen corners square.
- [x] Remove obsolete blur invalidation and entrance-refresh code while retaining authentication, clock transitions, and power controls.
- [x] Build the shell and pass the lock-surface configuration test; deploy and capture the real login screen with a sharp background and square display corners (`artifacts/login-2026-10-04/login.png`). Authentication was not exercised during this visual check.

## Task overview corners and theme — 2026-10-04

- [x] Mask the composed preview to its rounded frame so titlebars and thumbnails cannot overflow the corners.
- [x] Read the exported palette from the correct KConfig group, serialize theme exports, and explicitly refresh the running overview effect.
- [x] Version the effect package to v12 to bypass KWin's cached QML components; update installation, startup, and shell integration references together.
- [x] Preserve antialiased corner-mask coverage and enable smooth texture sampling. All 29 Qt tests pass, including an edge-coverage regression test; build and package/install checks pass. Inspect the deployed overview capture (`artifacts/task-overview-2026-10-04/smooth-corners.png`) to confirm smooth curved corners.
- [x] Validate with the shell build, 343 main-suite tests (one opt-in live test skipped), 28 Qt tests including rendered corner pixels, and both package/install checks.
- [x] Deploy in the running native session and inspect light/dark overview captures. Live palette refresh was exercised directly through effect configuration; the Settings theme interaction was covered by automated palette tests, not a manual click. Restore the original light palette afterward.

## Launcher scrolling — 2026-10-04

- [x] Scroll Favourites, the divider, the All Apps heading, and application tiles together in one browse viewport; retain grid virtualization and the fixed search/footer.
- [x] Build the shell and pass all 32 headless workflow tests, including header scrolling and bounded tile realization.
- [-] Confirm pointer scrolling in the running native desktop after reload; the existing native startup blocker remains documented below. The active desktop was not restarted for this change.

## Codebase cleanup review — 2026-10-04

- [x] Review architecture, working-tree changes, dependencies, and cross-language references before removing disconnected Core/System services and unused shell helpers.
- [x] Remove the unused color dependency and obsolete serialization option fields; preserve active integrations, UI behavior, native code, and recovery paths.
- [x] Restore the missing windowed-development session entry and fix application-free icon theme lookup hanging on the UI dispatcher, with regression coverage.
- [x] Validate focused Core, service, shell, and headless suites plus packaging/session checks.
- [!] Native startup smoke: one isolated layer-shell run passed, but a repeat and a separately rebuilt pre-cleanup baseline both aborted inside native HarfBuzz font creation. Runtime acceptance remains unresolved; see the cleanup review.
- [!] Full repository and formatting gates remain blocked by missing Avalonia designer `dnx` tooling and existing formatting/ShellCheck issues. Physical desktop acceptance and ISO payload regeneration remain unverified.

Removal evidence, validation boundaries, and retained complexity: [cleanup review](docs/validation/codebase-cleanup-2026-10-04.md).

## Daily workflow polish — 2026-09-30

- [x] Complete the Phase 1 product-design audit: map shell/app surfaces, shared components, top-task step counts, and visual/interaction inconsistencies before changing the design system. See [design audit](docs/design-audit-2026-09-30.md).
- [x] Phase 2 design tokens: established and documented one runtime source for color, typography, spacing, radius, elevation, motion, and sizing; migrated shared controls plus all rendered color and motion literals, added AA contrast coverage, and wired Reduce Motion to zero-duration resources. Feature component geometry adopts the canonical scale during Phase 3.
- [x] Phase 3 shared components: completed the common state matrix, normalized typography/icons/simple radii, raised semantic controls to canonical targets, unified common feedback layouts, expanded UI Kit coverage, and documented the narrow set of feature-specific structural variants.
- [x] Make Launcher Enter resolve the current query; preserve active Files tabs when closing a background tab; add tab, search, refresh, and recovery shortcuts.
- [x] Wire Notes autosave, atomic writes, visible save errors, retry, and unsaved-close protection; retain notes when deletion fails.
- [x] Improve file-picker keyboard navigation, search recovery, stale-request handling, thumbnail lifetime, selection validation, and narrow layouts.
- [x] Keep Wi-Fi connection cancellable, preserve the exact password, and guard duplicate submissions and late responses.
- [x] Guard App Store requests against stale results; add error/retry and empty states, adaptive card columns, and bundled navigation/catalog icons.
- [x] Use shared semantic colors for selected navigation and fix unresolved surface, accent-content, border, and danger brush references.
- [-] Complete native KWin interaction and visual acceptance across the remaining shell surfaces. Headless frames and regression tests do not establish compositor blur, physical input, scale, or performance acceptance.
- [!] Full repository gate: Avalonia designer build requires unavailable `dnx`. Git integrity also reports empty objects and a broken checkpoint reference; preserve working files while investigating separately.

Coverage, reproducible commands, and remaining checks: [UX validation report](docs/validation/ux-polish-2026-09-30.md).

## Existing implementation priorities

The app registry, launcher search, pinned apps, and core system status services (Milestone 3 and Milestone 4) are complete — do not re-implement them. Continue from there.

Recommended next implementation tasks, in priority order:

1. **Continue Milestone 6 runtime gates:** automated A1, A2, A4, and A5 plus the previously observed A3 now pass. Execute the remaining interactive B-F rows, then use a disposable QEMU image for G1-G5. Keep native-shell transport, missing `foot`, and other blocked rows visible; do not mark runtime checks complete from source inspection.
2. **Prepare packaging without publishing:** ownership, config preservation, service safety, and signature policy now have executable checks. Resolve the explicitly reported license, checksum, and real repository-host blockers before a clean disposable package build; do not modify the host package database or publish yet.
3. **Continue desktop gaming foundations:** close documentation and safe detection gaps before adding performance toggles or game-specific mutation.
4. **Keep release automation healthy:** use the Debug/Release scripts for all substantial changes and keep the changelog plus release notes current.

Keep the existing system bar and volume menu UI stable unless the user explicitly asks for a design change.

Do not start ISO generation until the shell app builds, runs locally, and the roadmap's shell/service checkpoints are reviewed.

Do not implement installer, partitioning, formatting, or disk changes.

After completing any task, run:

bash
dotnet build
scripts/test.sh
scripts/format-check.sh


Then update `ROADMAP.md` with completed and blocked items. Update `README.md` only if user-facing behavior or setup instructions changed.

---

# Notes Log

Use this section for longer implementation notes. Keep entries here detailed; keep the Changelog below terse (one line per change, cross-referencing a date here if more context is needed).

## Notes

- 2026-07-03: Current shell builds locally with .NET SDK 10. Theme and volume settings persist to `~/.config/DaisyOS/settings.json`.
- 2026-07-03: Real system volume integration uses `wpctl` against `@DEFAULT_AUDIO_SINK@`; missing command failures should remain non-fatal.
- 2026-07-03: Compositor direction clarified: KWin Wayland is the preferred first serious target for DaisyOS desktop behavior, with Xorg as fallback and Hyprland for optional visual experiments.
- 2026-07-03: Desktop-first gaming support added as a roadmap phase. Gaming must not replace the normal desktop experience.
- 2026-07-09: Added `IGamingService` with `LinuxGamingService` for GPU detection, Vulkan, GameMode, MangoHud, Gamescope status.
- 2026-07-09: Gaming Settings page now shows live detection cards instead of static "Planned" placeholders.
- 2026-07-09: `MockAppLauncherService` now also discovers real `.desktop` apps from the filesystem, which is the real app registry/model tracked in Phase 7.
- 2026-07-09: Added idle lock timer (5 min) to lock screen flow.
- 2026-07-09: Publish script improved with smoke test instructions and binary size output.
- 2026-07-09: Wi-Fi connect/disconnect UI fully wired in Settings.
- 2026-07-10: Reviewed the File Manager foundation and added functional inline rename plus hidden-file visibility controls.
- 2026-07-10: Expanded Files with breadcrumbs, direct path entry, mounted locations, permission details, and safe text previews.
- 2026-07-10: Roadmap audit — reconciled Phase 7 checklist with actual app-registry/launcher state, marked GPU/Vulkan/GameMode/MangoHud/Gamescope detection items complete in Phase 22 to match the live Gaming Settings cards, closed out Milestone 5's file-manager/notification-center/app-launcher items, updated `docs/` file list to include `testing.md` and `window-controls.md`, and rewrote "Immediate Next Task" to point at Phase 23 (testing) and the remaining Milestone 5 items instead of already-finished work.
- 2026-07-12: Completed Milestone 5 with versioned onboarding, reopenable Welcome, read-only updates, default-app recommendations, and report-only app policy decisions. Reconciled the corresponding Phase 20, Phase 23, Phase 24, and milestone checklists.
- 2026-07-12: Added the 0.1.0 release foundation: centralized informational versions, repeatable Debug/Release scripts, versioned self-contained Linux x64 archives, SHA-256 verification, CI validation, nightly artifacts, tag/manual beta and stable workflows, changelog, release notes, and recovery guidance. ISO creation, signing, Arch package publication, and automatic updates remain future work.
- 2026-07-13: Completed the gaming diagnostics reliability slice. Settings now checks Wine, 32-bit Vulkan readiness, controller rules, and Bluetooth service state alongside the existing GPU, Vulkan, Steam, GameMode, MangoHud, and Gamescope checks. Results stay read-only, use plain-language failure states, expose a collapsed copyable support report, and have focused automated coverage. Reconciled the existing Gaming Mode, Quick Settings, notification suppression, package-profile, and guidance work with the Phase 22 checklists.
- 2026-07-13: Completed the repository side of the Milestone 6/Phase 23 validation slice. The opt-in KWin profile now carries deterministic native Wayland, XWayland, clipboard, portal/audio test tools; a read-only preflight emits a shareable Markdown report and supports strict VM gating; fixture tests cover healthy and broken sessions; and the acceptance matrix defines evidence for windows, decoration, blur, focus, display scaling, portals, gaming, recovery, and fallback. Five Welcome ViewModel tests cover navigation, completion, mode-specific exit behavior, and service failures; onboarding now hides internal exception text behind short recovery guidance. No real KWin or QEMU runtime result is claimed.
- 2026-07-13: Executed the first real nested-KWin gates with the published self-contained shell. KWin 6.7.2 opened a contained 1440x900 Wayland output, started XWayland and DaisyOS, and passed strict preflight with zero failures or warnings. Interactive behavior rows and disposable-QEMU recovery remain unclaimed. Strengthened Arch packaging validation around owned paths, preserved administrator configuration, safe service lifecycle, version alignment, and required package/database signatures; the generated readiness report honestly blocks publication on the undecided license, placeholder checksums, and example repository host.
- 2026-07-14: Converted the nested KWin startup check into a repeatable bounded runtime gate. The harness verifies the exact nested compositor PID and socket with a native Wayland round trip, starts the production DaisyOS shell and an XWayland xterm client, captures strict preflight with zero failures/warnings, exits only the nested process tree, and re-probes the parent graphical session. The shell still uses Avalonia's X11 backend through XWayland; native shell transport, interactive B-F rows, and QEMU recovery G1-G5 remain open.
- 2026-07-13: Completed the cross-shell state and failure-presentation pass. A shared notification policy now guarantees error and critical visibility while respecting quiet modes for normal notifications. Shared calm failure messages replace raw exceptions and command results in Files, Desktop, Launcher, Quick Search, Power, Trash, and app-launch paths. Settings adds explicit network loading/offline/unavailable panels backed by a truthful availability flag, and the Linux network service no longer returns command output as UI detail. Welcome adds distinct Wi-Fi/update icons and error surfaces. Contract and service tests cover the state bindings, policy matrix, wording, raw-exception prohibition, and network outcomes.
- 2026-08-07: Completed the dual-theme redesign and performance optimization pass. Added a daytime paper-inspired Light Theme (`#F4F5F7` base, soft charcoal `#1F2328` text, `#FFFFFF` cards, `#F5F5F5` card hover, `#12000000` 7% floating bar button hover overlays) alongside a normalized Dark Theme (`#1E1E1E` chrome, 8% white hover highlights). Optimized `WallpaperColorExtractor` with a strided 64x64 grid sampling method and seed caching, reducing cold extraction time on 6K/8K images from ~4s to ~15ms (<0.01ms cached). Redesigned `SearchBar` and `CaptionClose` buttons for 1:1 color matching with Settings buttons and title bar controls. Synchronized audio spectrum visualizer bars with media control accents. Excluded `TaskbarBorderBrush` from dynamic theme color extraction. Passed all 82 unit tests.

---

# Changelog

## Unreleased

- Added trackpad scroll speed controls to Settings > Mouse & Trackpad: implemented a 5-tick discrete QuickSettingsSlider matching the Taskbar size slider pattern (Middle tick default: 1.0, Slowest: 0.4, Slower: 0.7, Faster: 1.4, Fastest: 1.8), with tooltips, search indexing, live D-Bus property updates to active KWin touchpad devices, and persistent configuration in `kcminputrc`.
- Added power & sleep management in Settings with display timeout, system sleep timeout, auto-lock controls, and battery-aware live wallpaper throttling; optimized OS memory footprints with glibc malloc arena caps, THP madvise, VM cache reclaim tunables, and desktop idle trimming.

- Improved daily search, Files, Notes, file-picker, Wi-Fi, and App Store workflows; added interaction/render coverage and corrected missing theme resources and icons. See the 2026-09-30 UX validation report.

- Added JSON-backed shell settings.
- Added real audio volume read/write service through PipeWire `wpctl`.
- Updated README and roadmap to match current DaisyOS project state.
- Replaced remaining DeskOS naming with DaisyOS.
- Fixed Desktop-First Gaming Support markdown formatting.
- Added KWin Wayland compositor direction.
- Added Default Applications phase.
- Added Core System Apps and shared DaisyOS app window framework.
- Added `IGamingService` and `LinuxGamingService` for GPU/Vulkan/GameMode/MangoHud/Gamescope detection.
- Gaming Settings page now shows live detection cards instead of static placeholders.
- `MockAppLauncherService` now discovers real `.desktop` apps in mock mode too.
- Added idle lock timer (5 min) to lock screen.
- Publish script improved with smoke test instructions and binary size.
- Wi-Fi connect/disconnect UI fully wired in Settings Network page.
- File Manager now supports validated inline rename and showing or hiding Unix dotfiles.
- File Manager now includes breadcrumb/path navigation, mounted locations, permission details, and bounded text previews.
- Roadmap audit: reconciled checklist/notes drift in Phases 7, 18, 22, and Milestone 5; updated `docs/` listing; rewrote "Immediate Next Task" guidance.
- Restored the Files destructive-action flow and added the first automated command-runner and settings-persistence test suite.
- Refactored the shell onto Avalonia Fluent controls with centralized DaisyOS colors, typography, and semantic control variants.
- Completed versioned first-run onboarding, reopenable Welcome, report-only app policy status, and the read-only update experience.
- Added centralized 0.1.0 build metadata, Debug/Release scripts, versioned Linux artifacts, checksums, CI, and release documentation.
- Added safe daily update checks, persistent check history, update notifications, and reboot-required status without enabling package installation.
- Expanded read-only gaming diagnostics, added a copyable advanced report and focused tests, and reconciled completed Gaming Mode/package-profile roadmap items.
- Added compositor-session preflight reporting, nested/QEMU acceptance coverage, Welcome ViewModel tests, and calm onboarding service-failure messages.
- Completed shell-wide loading, empty, offline, unavailable, and error presentation with calm failure wording and automated contracts.
- Added BIOS/UEFI stability smoke tests, conditional native-crash diagnostics, and real-menu Restart/Shut down VM tests; retained the intermittent TCG renderer failure as an explicit open gate.
- Added a repeatable nested-KWin isolation gate with exact-process preflight, real Wayland socket probing, XWayland client startup, host-session survival checks, and explicit interactive/native-shell blockers.
- Redesigned and perfected Light and Dark theme systems with warm paper Light mode (`#F4F5F7`), normalized Dark mode (`#1E1E1E`), 1:1 SearchBar/Settings button alignment, and high-contrast window title bar close controls.
- Optimized wallpaper color extraction performance for high-res/6K images with strided 64x64 sampling and seed caching (~15ms cold, <0.01ms warm).
- Synchronized MediaWidget audio spectrum visualizer bars with media control accents across theme toggles.
- Standardized Settings card buttons to 8px corner radius and set 1px exact Quick Settings split pair button dividers.

### Settings and Notes custom window chrome (2026-10-04)
- [x] Share `AppWindowTitleBar` between Settings and Notes, reusing Files' KWin-matched `WindowCaptionGlyph` paths and theme caption dimensions. Replace the unused Notes-only titlebar with this shared component.
- [x] Extend both navigation sidebars to the full content-frame height and use `SurfaceRaisedSubtleBrush`, matching the visible Settings group boxes. Keep Notes save errors in the editor pane and preserve unsaved-close protection.
- [x] Preserve native move, maximize/restore, minimize, close, edge resizing and F11/Escape fullscreen behavior; remove rounded frame/gutters in maximized/fullscreen states.
- [x] Request client decorations before initial Wayland window creation and extend the installed initial no-border rules and native bridge to Settings/Notes.
- [x] Validation: native rebuild completed with zero warnings/errors; all 35 headless tests passed, including both themes, full-height sidebars, maximize/fullscreen and Notes save protection. Live KWin window-added traces show Settings and Notes initially `noBorder=true`; screenshots confirm single custom titlebars and full-height matching sidebars. Pointer-driven drag/resize and individual presented startup frames remain manual acceptance checks.

### Caption-only titlebars and full-height Files navigation (2026-10-04)
- [x] Remove app icons/titles and their unused properties from the shared Settings/Notes titlebar; retain native window titles for task overview and window identification.
- [x] Move Files navigation beside the entire main pane so its sidebar spans tabs, toolbar, file area and status bar; match the Settings/Notes sidebar `SurfaceRaisedSubtleBrush` in both themes. Preserve navigation, pinning/unpinning, volumes/eject, tabs, search and file actions.
- [x] Remove Files' duplicate compositor fullscreen controller; the existing custom chrome owns F11/Escape and frame presentation.
- [x] Validation: deployed native build passed; 35 headless tests (now including Files sidebar/theme/fullscreen layout) and 9 focused Files/chrome checks passed; `git diff --check` passed. Live Files/Settings screenshots checked; Files remains borderless in KWin. Pointer navigation and every presented startup frame were not individually verified.

### Adaptive Files tabs and taskbar capacity (2026-10-04)
- [x] Replace fixed-width, horizontally scrolling Files tabs with an equal-width fitting panel, retaining the normal 166px width when space permits. Keep new-tab/window controls reserved, truncate titles with full-title tooltips, and hide location glyphs/labels at compact widths to preserve tab close controls.
- [x] Cap the taskbar at the `TaskbarMaximumWidth` token (1200 logical pixels, screen-clamped with 12px side clearance). Shrink app hit slots, icons/indicators and gaps together after the cap; restore normal sizes as items close. Preserve Start, dragging/reordering, context menus and window actions with current slot geometry.
- [x] Validation: all 37 headless tests passed, including 12-tab close/restore sizing and 40 extra taskbar items in an 800px window; 8 focused Files/taskbar checks passed; native rebuild succeeded with zero warnings/errors; `git diff --check` passed. In the live OS, opened seven extra Files tabs via Ctrl+T, visually confirmed all eight fit, closed those seven via Ctrl+W and confirmed normal width returned. Crowded taskbar fitting was verified in headless controls; the running dock was checked at its current item count.

### Compositor-owned app frames, pointer resizing and active Quick Settings icons (2026-10-04)
- [x] Remove client-painted outer/highlight borders, shadows, rounded clipping and transparent frame gutters from Files, Settings and Notes. Give the file picker an ordinary KWin titlebar/frame; other normal system apps already use the native titlebar plus the same compositor effect.
- [x] Replace Files' eight custom handle controls/handlers and Settings/Notes gutter-only hit regions with shared 6px visible edge/corner targets and native resize cursors. KWin owns the pointer grab and dimensions; fullscreen/maximized windows do not offer client resize targets.
- [x] Remove the broad process exclusion from the frame-effect installer/live config and Files-specific eligibility exception from the pinned effect patch. Untitled layer surfaces remain excluded by the effect's surface guard, with lock-screen exclusions retained.
- [x] Use accent-colored active Quick Settings icons, including split-detail arrows; keep active labels and tooltip text neutral. Validate both themes with transitions disabled only in the color-contract test.
- [x] Validation: native rebuild passed with zero warnings/errors; 362 main tests passed (one opt-in live-capture skip), 37 headless tests passed; frame patch applies to pinned source; package checks and `git diff --check` passed. Live mouse drags widened Files by exactly 120px, then restored it via -80/-40px; Settings followed +90/-90px. Both report resizable/borderless in KWin. Live screenshot checked compositor frame and active Quick Settings icon/label colors. Other apps' complete pointer interactions were not individually exercised.

## Standalone Files application — first migration pass

- [x] Add `DaisyOS.Files` executable with its own dispatcher, renderer and window lifetime; remove Files window ownership from the shell.
- [x] Discover Files through its desktop entry and launch it as an external application.
- [x] Forward repeated activations through a user-only pipe, with a global per-user/display ownership mutex.
- [x] Move freedesktop file-manager activation into Files; install on-demand D-Bus activation without replacing third-party configuration.
- [x] Preserve shared UI/theme resources and route LocalSend/desktop feedback through shell IPC.
- [x] Include Files in development builds, release publish and ISO staging.
- [x] Build, main tests (364 passed, 1 opt-in capture skipped), UI tests (37 passed), package installation and release-script checks passed. Live KWin confirmed an independent Files PID/app ID, repeated activation selected a file in the existing process, cold D-Bus activation launched Files after reloading bus metadata, and Files retained its PID/window across a shell restart. A self-contained Release publish (ReadyToRun disabled) also ran successfully in the native session.
- [ ] Extract shared UI resources into a dedicated library in a later subsystem pass. Files, Settings and Notes still share the Avalonia backend.


### Standalone Notes and Settings (2026-10-04)
- [x] Give Notes and Settings independent executable projects, dispatchers/renderers, ordinary Wayland toplevels and main-window lifetimes. Preserve existing custom captions, full-height sidebars and Notes save/close protection.
- [x] Forward repeated activations to the existing instance through user-only pipes, retaining new-note and Settings category navigation. Launch through ordinary desktop entries instead of the shell's built-in app catalog.
- [x] Keep desktop-owned appearance, wallpaper and shell-state writes in the desktop through confirmed, serialized IPC; keep existing Linux hardware integrations in Settings and refresh application appearance/state snapshots.
- [x] Include all three standalone applications in development installation/restart, self-contained publishing and ISO desktop launchers.
- [-] Validate automated checks and running-OS behavior for this migration.

### GUI polish and shared file browsing (2026-10-06)
- [x] Fit widget-library previews into a compact grid without scrollbars; simplify photo size labels and use clean rounded image crops.
- [x] Use plain language in Settings and Files failure messages while retaining technical diagnostics in logs.
- [x] Give the shared file picker a full-height sidebar, support unrestricted files and caller-specific extension filters, and pack search results without empty slots. Route Notes, sharing and wallpaper selection through the shared picker.
- [x] Share the 4px navigation-item gap between Settings, Files and the picker; use black/white high-contrast today markers in both calendar surfaces.
- [x] Check actual extraction-command results before reporting success and protect destination creation with the extraction error handler.
- [x] Validate app builds and tests: 373 main tests passed (one opt-in capture skipped), 92 GUI tests passed, 125 shell tests passed, and 11 final focused UI tests passed. All 12 repository script checks passed; native session preflight reported no failures or warnings, and the shell was reloaded. Both themes were inspected through rendered GUI test frames.
- [!] The complete solution gate remains blocked by Avalonia DesignerSupport’s build dependency: `dnx` is absent from PATH; a temporary `dotnet dnx` wrapper reaches Bun but it exits with code 132. The existing Avalonia checkout remains unchanged.

### Pointer speed icon and file drag badge (2026-10-06)
- [x] Use the supplied pointer_speed.svg for Settings, with currentColor for both themes.
- [x] Render a compact blue filename badge on the native Wayland drag surface, including multi-file counts, ellipsis and display-density scaling. Keep compositor-owned pointer movement and real drop effects.
- [x] Shell build, SVG resource check and three drag-preview sizing tests passed; inspected the rendered badge and reloaded the shell. Native cross-app drag appearance still requires a live check. The installed /usr/local restart helper was stale and rebuilt only Shell; use the repository helper to rebuild standalone apps. Updating the system-wide helper requires administrator authentication.

### Custom capsule toggles (2026-10-06)
- [x] Synchronize thumb travel and track color with a 100ms cubic transition, retain the dragged position when releasing, and honor Reduce motion through control-level transition resources. Avoid rebuilding Input settings after successful toggle changes. Fourteen focused toggle and taskbar checks passed.
- [x] Replace the Fluent-derived toggle visuals with a shared, independently editable capsule theme: accent track, gray off track, pale outlined thumb, 56×24 geometry with a 28px thumb and 24px travel, smooth travel, and a focus-only ring.
- [x] Preserve native ToggleSwitch bindings, keyboard, thumb dragging and accessibility; share Reduce motion durations and theme-aware off colors.
- [x] Build and seven focused interaction/layout checks passed in both themes; the compact-width follow-up passed both click/keyboard/drag checks and was inspected in a rendered frame. Rebuild standalone applications with the repository restart helper; existing app windows require reopening to load new resources.

### Taskbar stacked-window indicator (2026-10-06)
- [x] Cut the foreground rounded tile out of a full-height 40×40 backing tile shifted 6px right, with a 2px gap following the foreground rounded edge. Avoid overlap and transparent-background bleed-through.
- [x] Match the main content presenter's focused, hovered and pressed fill; show a neutral filled tile when inactive. Remove outlines and extra backing frames.
- [x] Reserve grouped-window space in dock sizing and reorder positions; restore normal spacing when windows close.
- [x] Shell build and nine taskbar preview/state/geometry checks passed; inspected light/dark active/inactive frames. Reload the shell through the repository helper.

### Widget library preview proportions (2026-10-07)
- [x] Remove sunken preview wrappers and retain the widgets’ own surfaces. Scale every preview from desktop grid dimensions with one shared scale, preserving medium, large and extra-large proportions across categories. Correct the five-column media footprint and fit the photo bento layout with rounding allowance.
- [x] Both focused layout checks passed across all four categories in light/dark and narrow/standard layouts, checking bounds, overlap, transparent wrappers and relative sizes; inspected rendered clock and photo previews.

### Compact file drag preview (2026-10-07)
- [x] Match compact Files rows with a 26px preview slot and 13px label using the window font. Render the selected entry thumbnail or application icon with preserved proportions; retain the semantic file icon only as a fallback. Remove the decorative icon tile and reduce the badge height from 44px to 34px.
- [x] Five focused checks passed, including real thumbnail pixels, bounded labels and fractional display density. Inspected rendered badges; native pointer placement remains compositor-owned.

### Photo widget library placeholders (2026-10-07)
- [x] Replace bundled sample photographs with neutral photo placeholders, a “Your photos” label and guidance to place the widget before choosing photos. Preserve the shared preview scale and all three size options.
- [x] Both widget library layout checks passed, including no Image controls in photo previews; inspected the narrow light-theme rendering.

### User-owned wallpapers (2026-10-07)
- [x] Remove Assets/Wallpapers, sample photo defaults, bundled asset deployment and legacy asset-path searches. Use an opaque solid-color default/fallback, resolve user paths/file URLs, and let photo widgets ignore unavailable saved images. Remove stale wallpaper copies from generated build output.
- [x] Preserve native video playback tests using a 1.7KB test-only fixture. Fifteen wallpaper checks and three widget layout/empty-state checks passed; user-selected files remain supported in Settings.

### Widget snap destination outline (2026-10-07)
- [x] Draw a non-interactive accent outline behind dragged widgets at their resolved committed drop location, matching the widget frame inset and XXL radius. Use the existing reorder result for swaps, blocked targets and edge clamping; clear the outline on release, cancellation and capture loss. Cover desktop and library drags through the shared controller.
- [x] Three focused drag checks passed for different widget sizes, edge clamping, blocked placement, exact release alignment and cancellation; inspected the rendered outline behind a dragged card.

### Restore lock-screen clock blur (2026-10-07)
- [x] Restore cached glyph-shaped KWin blur regions for both clock crossfade layers, retaining avatar/password regions and the transparent foreground over the separate wallpaper surface. Follow clock geometry, opacity, font and text updates on primary and secondary displays.
- [x] Shell build and the focused glyph/animation/visibility check passed. Confirmed the live KWin blur effect is loaded; live lock-screen appearance requires checking on the next lock.

### Smooth widget snap outline (2026-10-07)
- [x] Ease the outline between resolved snap bounds using an interruptible 160ms cubic transition. Start each drag at its initial destination, clear in-flight motion on cancellation/release, and honor Reduce motion.
- [x] Four focused checks passed, including intermediate positions, smooth retargeting, reset behavior, Reduce motion, blocked targets and release alignment.

### Cursor settings, Steam launch and background-app tray (2026-10-07)
- [x] Send KWin's cursor-specific reload signal after persisting theme and size; use compositor-owned standard cursor shapes in the native Avalonia backend.
- [x] Resolve the current compositor's XWayland display and authority for application launches instead of guessing a global X socket. Steam opens a real window on the live session's display.
- [x] Add a System Bar background-app tray using real StatusNotifier registrations, app icons, activation and application menu actions; rotate the upward chevron 180 degrees around its center with shared motion timing and reduced-motion handling.
- [x] Final focused checks: seven backend and five GUI checks passed, including intermediate chevron rotation, fixed center, reversal, reduced motion, watcher coexistence and menus. Native rebuild/restart passed; live Settings cursor read-back and Steam tray/menu registration confirmed. See `docs/validation/cursor-steam-tray-2026-10-07.md` for evidence and limits.
- [!] Main suite retains the pre-existing widget icon-token failure; complete solution validation retains the missing designer `dnx` prerequisite. Git status/diff is unavailable because HEAD's loose object is corrupt.

### Compact control and confirmation polish (2026-10-07)
- [x] Standardize desktop action buttons, single-line inputs, dropdowns, and search controls at 32px using shared semantic tokens; migrate local desktop action overrides and the widget library Done button.
- [x] Simplify confirmations with a smaller heading, secondary explanation, quiet Cancel default, rightmost destructive action, and no minimize/maximize controls.
- [x] Remove the Settings wallpaper preview border. Center press transforms on the entire button and remove independent content scaling; preserve the Notes toolbar container space for its controls.
- [x] Five new rendered checks pass across light/dark themes and pressed-label geometry. Core suite: 376 passed, one skipped; all new polish checks and the focused launcher-icon rerun pass.
- [x] Native standalone builds and shell reload passed; live tray rendering and strict compositor preflight checked with zero failures/warnings. Final GUI suite: 119 passed, one launcher-icon timeout; all new polish checks pass. See `docs/validation/control-polish-2026-10-07.md`.
- [!] Full GUI runs expose an order-sensitive launcher icon loader failure (passes isolated); the complete solution gate retains the missing designer `dnx` tool. Git HEAD remains corrupt, so task-specific backups and a standalone patch preserve the review baseline.

### 2026-10-07 — Desktop language and font support

- [x] Research Avalonia/.NET localization, runtime switching, Linux user locales, font fallback, shaping and pixel alignment; record decisions and primary sources in `docs/architecture/languages-and-typography.md`.
- [x] Add English/Finnish resources, neutral fallback, live resource bindings, culture-aware formatting and a persisted System language picker under Language & input.
- [x] Pass the user's language to new applications through the process and D-Bus/systemd environment; keep keyboard layout and system-command parsing independent. Add safe locale imports to session sources and generated Finnish UTF-8/CJK/emoji support to the ISO.
- [x] Share whole-pixel typography, measured line heights, font fallback, grayscale antialiasing, strong hinting and aligned baselines across shell and standalone apps. Grow controls consistently for larger fonts; preserve 32px normal Inter controls.
- [x] Validate language persistence/fallback, multilingual shaping, narrow Settings, translated confirmations and real native language switching; see `docs/validation/language-support-2026-10-07.md`.
- [!] Refresh the installed root-owned native development-session helper for next-login locale imports. Source and installer tests pass; passwordless sudo is unavailable. Run `sudo scripts/install-native-development-session.sh` from this checkout. Current shell and new application launches already use the language preference.

Notes: English and Finnish are the initial supported UI languages. Other catalogs, CLDR plural forms and RTL visual acceptance must be completed before offering additional languages. Arbitrary damaged fonts and every physical fractional-DPI monitor combination cannot be guaranteed by headless rendering tests. The complete solution gate remains blocked by the bundled Avalonia `dnx`/Bun tool requirement; the launcher-icon suite has a pre-existing order-sensitive timeout which passes in isolation.

### 2026-10-07 — Lock-screen sizing and OS updates

- [x] Raise clock opacity to 50%; make clock, credential and footer sizing responsive to screen size, font changes and translated text, with scrollable compact layouts and clipped glyph blur.
- [x] Five focused rendered lock-screen and animated glyph-mask checks passed; see `docs/validation/lockscreen-polish-2026-10-07.md`.
- [x] Verify installed Linux 7.2.9 is already the current stable release; update Flatpak applications and OS runtimes successfully.
- [x] Administrator-authenticated full Arch transaction installed/upgraded 162 packages; the repository upgrade list is empty. All 21 hooks completed; command exit 0. No reboot performed.

### Lock-screen visual balance follow-up (2026-10-07)

- [x] Narrow the normal password pill to 272px, use a fully circular 48px avatar and tighter 8px credential spacing, and lift the group with proportional bottom clearance.
- [x] Show password help when the hint button is used; preserve error/Caps Lock feedback, larger-font sizing, compact-screen scrolling and authentication safeguards.
- [x] Five focused rendered checks passed, native builds and shell reload completed, and strict compositor preflight returned zero failures/warnings. Physical lock-screen visual acceptance remains with the user.

### Accent swatches and Quick Settings consistency (2026-10-07)

- [x] Keep accent swatches circular at 24px, overriding generic minimum height; retain each swatch background through hover and press, with a subtle opacity response and selection ring.
- [x] Share 44px normal height between single/split Quick Settings controls and the volume container, including larger-font growth.
- [x] Ten focused GUI checks pass, including light/dark swatch hover colors and split-control geometry.
- [x] Native builds and shell reload completed. Strict compositor preflight: zero failures/warnings. See `docs/validation/swatch-quick-settings-2026-10-07.md`.

### Readable input height follow-up (2026-10-07)

- [x] Raise shared single-line text fields, dropdowns and search surfaces to 40px, with larger-font growth and centered text.
- [x] Migrate local app input sizing and generated picker height bindings; retain standard action button sizing and circular swatches.
- [x] Seventeen focused GUI checks pass, including input/search geometry, larger fonts, Finnish layout, swatches and Quick Settings.
- [x] Native build/reload succeeded; compositor preflight has zero failures/warnings. See `docs/validation/input-height-2026-10-07.md`.

### System button size variants (2026-10-07)

- [x] Add shared Small (32px), Medium (36px), and Large (42px) button/toggle variants, preserving the supplied Files sidebar and widget library Done reference heights.
- [x] Apply Large to Files, Settings and Notes sidebar navigation, including pinned Files row containers. Mark widget Done Small; grow all variants with font metrics.
- [x] Sixteen focused GUI checks passed, including distinct rendered size variants, widget layout, translated Settings and larger-font rendering.
- [x] Native builds/reload completed; compositor preflight returned zero failures/warnings. See `docs/validation/button-size-variants-2026-10-07.md`.

### Functional desktop Trash (2026-10-07)

- [x] Choose themed Trash or Trash_Item assets from real trash/files contents; refresh after desktop moves and poll while the desktop is loaded for external empty/restore operations.
- [x] Reserve Trash during layout/reorder, exclude it from passive FLIP animations, and retain normal reordering when Trash itself is dragged.
- [x] Support desktop pointer drops and external file-manager drops to Trash through the shared restorable operation; preserve desktop items on failure and reserve .trashinfo before moving data.
- [x] Thirty-three desktop checks pass, including real owned-file pointer drop and restore metadata under Finnish culture.
- [x] Native build/reload succeeded; strict compositor preflight returned zero failures/warnings. See `docs/validation/desktop-trash-2026-10-07.md`. Physical pointer acceptance remains with the user.

### Theme synchronization and smooth appearance changes (2026-10-08)

- [x] Follow mutable SVG foreground paints, propagate popup icon foregrounds, resolve the actual theme variant, and release brush subscriptions on detach.
- [x] Replace Files sidebar/tab one-time brush conversion with live semantic resource bindings. Notify standalone apps promptly after shared preference changes, retaining polling fallback.
- [x] Add shared color transitions and image cross-fades while preserving interaction animations, original image bindings, rapid retargeting and reduced motion.
- [x] Six focused GUI regressions pass, including rendered menu icon colors in both themes.
- [x] Affected GUI checks pass 22/22; core suite passes 382 tests with one live screenshot skip. Standalone builds and shell reload succeeded; strict compositor preflight returned zero failures/warnings.
- [!] Full GUI run passes 139/140 with the previously documented launcher-icon refresh timeout; its isolated rerun passes. Physical theme-switch acceptance remains with the user. See `docs/validation/theme-synchronization-2026-10-08.md`.

### Tooltip corner transparency (2026-10-08)

- [x] Exempt opaque tooltip popups from the native menu blur mask before positioner replay, preserving menu blur and deferred popup attachment.
- [x] Thirteen focused GUI checks pass, including tooltip corner alpha in both themes at normal/fractional scale.
- [x] Standalone builds and native shell reload succeeded; strict compositor preflight returned zero failures/warnings. Reopen existing system-app windows to load the updated backend. Physical corner appearance remains a user acceptance check. See `docs/validation/tooltip-corners-2026-10-08.md`.

### Trash and logout hangs (2026-10-08)

- [x] Close the Trash menu before background deletion, share in-flight requests, preserve failed-item restore metadata and report translated errors.
- [x] Bundle an independent logout guard that validates session-owned children and bounds shutdown, including compatibility with the older installed supervisor. Bound source launcher cleanup waits and catch power-menu service exceptions.
- [x] Trash/desktop checks pass 29/29, GUI action checks 3/3 and core tests 384 with one live screenshot skip. Unresponsive logout fixtures preserve unrelated processes; session installer fixtures pass.
- [x] Native builds and shell reload succeeded with KWin preserved; strict compositor preflight returned zero failures/warnings.
- [!] Actual Empty Trash and logout were not executed on the user's session. Physical acceptance remains with the user. See `docs/validation/trash-logout-hangs-2026-10-08.md`.

### Popup opening transparency (2026-10-08)

- [x] Exclude the native popup host background from shared brush transitions so its initial white default cannot fade into transparency around ComboBox menus and tooltips.
- [x] Eleven focused GUI checks pass, including a native popup host regression, tooltip corners in both themes/scales and existing theme synchronization behavior.
- [x] Native system apps rebuilt with zero warnings/errors; shell reload preserved KWin and strict compositor preflight returned zero failures/warnings. Reopen existing system-app windows to load the fix. Physical opening-frame acceptance remains with the user. See `docs/validation/popup-opening-flash-2026-10-08.md`.
### Text clarity and display scaling audit (2026-10-08)

- [x] Verify grayscale antialiasing, full hinting, baseline alignment, logical font sizes, measured font-dependent geometry and native Wayland fractional scaling.
- [x] Extend shared typography/rendering policy to native popup roots. Prevent the popup template's background presenter from independently animating native-surface transparency.
- [x] Twenty-six focused GUI checks pass, covering render scales 100–300%, scale changes, alternate fonts, multilingual layouts, tooltip corners and theme synchronization. Inspect the larger-font rendering sample.
- [x] Native apps rebuilt with zero warnings/errors and shell reloaded with KWin preserved. Strict compositor preflight returned zero failures/warnings. Reopen existing app windows for the updated policy. Physical sharpness across other displays remains unverified. See `docs/validation/text-rendering-audit-2026-10-08.md`.
### Files caption alignment and stronger danger colors (2026-10-08)

- [x] Stretch Files caption buttons through the title row, removing the vertical inset while keeping glyphs centered.
- [x] Add saturated filled danger roles with distinct rest/hover/pressed colors and white content; use theme-aware error text for destructive menus and export stronger native close-button colors.
- [x] Fourteen focused GUI checks pass, including caption geometry and larger fonts. Core suite passes 384 tests with one live screenshot skip. White-content contrast is 5.43:1 or higher across danger action states.
- [x] Native apps rebuilt and shell reloaded, preserving KWin. Strict compositor preflight reports zero failures/warnings. Reopen existing Files windows for the updated title bar. See `docs/validation/files-caption-danger-2026-10-08.md`.
### Translated action label sizing (2026-10-08)

- [x] Size Files Sort and its toolbar to their translated content while retaining minimum dimensions. Remove fixed widths from translated Wi-Fi, Properties and App Store actions.
- [x] Seventeen focused GUI checks pass, including live English/Finnish changes at Files' minimum width and larger font sizing, plus multilingual Settings/dialog and App Store layouts.
- [x] Native apps rebuilt and shell reloaded with KWin preserved; strict compositor preflight reports zero failures/warnings. Reopen existing app windows to load the layouts. See `docs/validation/translated-action-labels-2026-10-08.md`.

### Everyday reliability audit (2026-10-08)

- [x] Run the full repository gate and review live session health; document evidence in `docs/validation/everyday-reliability-audit-2026-10-08.md`.
- [x] Bound command output, use atomic text persistence, lease/bound wallpaper source caches, coalesce identical wallpaper requests, and cache timezone lookup data.
- [x] Exercise wallpaper rendering with a real initialized renderer and add concurrency/resource regression coverage.
- [!] Native render CPU cost, long-running memory plateau, hardware/suspend/upgrade matrices, and population-scale reliability remain unverified; see the audit report.

### Profile picture setting (2026-10-08)

- [x] Add a localized Appearance picture picker, circular preview and removal action, with persisted shell-owned image copies shared by launcher and lock-screen avatars.
- [x] Bound imported file/pixel sizes, resize to 512 px, honor photo orientation, retain defaults for missing images, and test persistence/import/removal.

### Caption Close hover (2026-10-08)

- [x] Exclude Close from neutral caption hover/pressed styles across shared app chrome, Files, Calendar, and Calculator. Apply its semantic red immediately instead of animating through intermediate neutral fills.
- [x] Verify immediate/repeated hover in both themes and retain title-bar/theme checks: nine focused GUI tests passed.

### Fast hover feedback (2026-10-08)

- [x] Add a 50 ms hover timing distinct from longer theme/spatial motion, including reduced-motion support. Apply it to shared button/list backgrounds and Files/Settings interaction fills.
- [x] Let Files row presenters own hover/selection feedback, preventing compounded parent/presenter fades and trailing row highlights.
- [x] Pass 15 focused Files hover, caption, theme and toggle checks, including light/dark row clearing and retained selection.

### Files sidebar localization and user-directory paths (2026-10-08)

- [x] Translate standard sidebar names independently of navigation paths, including live language changes. Keep pinned folder/drive names literal.
- [x] Resolve standard locations from XDG user-directory configuration, preserving localized/custom paths and semantic icons; share the mapping with file pickers and sidebar drop targets.
- [x] Fix window-root localization refresh lifecycle; pass 13 path/icon checks and 13 GUI localization/sidebar/hover/picker checks.

### Background-app icon identity (2026-10-08)

- [x] Prefer desktop-application icons for normal tray entries using their published Id/title; retain tray-specific attention indicators and published-icon/pixmap fallback.
- [x] Prefer app-provided theme paths before global tray icon names and continue fallback when an individual icon cannot decode. Four focused tray tests passed.

### Shell slide animation polish (2026-10-10)

- [x] Give launcher, system flyouts, taskbar/systembar and notification toasts shared 220ms entrance and 140ms dismissal tokens, with decelerating entrance and accelerating exit curves.
- [x] Preserve position/opacity on reversal, shorten interrupted slides to remaining distance, and protect visibility against stale completion. Keep current viewport slide distances and opaque flyout exits.
- [x] Reveal launcher without waiting for icon decoding; ease taskbar/launcher/systembar transforms and use reduced-motion-aware taskbar reorder timings.
- [x] 24 focused motion, toast, taskbar, launcher, tray and Quick Settings checks pass. Two unrelated obsolete PickerEntry test files required temporary compile exclusion; details in `docs/validation/shell-motion-2026-10-10.md`.
- [x] Files, Notes and Settings rebuilt successfully; native shell reloaded. Strict KWin session preflight reports zero failures and warnings.
- [!] Physical frame pacing and perceived smoothness require acceptance in the running KWin session.

### Launcher first-frame slide correction (2026-10-11)

- [x] Reproduce first-open motion using unmeasured launcher geometry with a failing regression check.
- [x] Prepare layout and cached decorative frames while transparent, then start an opaque full-height slide below the viewport clip. Use a dedicated gentle 280ms entrance curve; retain 140ms dismissal and interruption continuity.
- [x] 10 focused first-frame, motion, icon, toast and frame-rendering checks pass; see `docs/validation/launcher-first-frame-2026-10-11.md`.
- [x] Files, Notes and Settings rebuilt; native shell reloaded. Strict session preflight reports zero failures and warnings.
- [!] Physical smoothness and moving blur still require display acceptance.

### Launcher and task overview shortcut reliability (2026-10-11)

- [x] Restore bare Meta to Launcher via the persistent KWin bridge, independently of overview QML loading.
- [x] Use Meta+Tab for Task Overview, matching Windows Task View; macOS uses Control+Up Arrow for Mission Control.
- [x] Migrate live KGlobalAccel ownership away from KWin's conflicting Meta+Tab window switcher and remove the old Alt+F1 overview binding.
- [x] Replace the failed/cached v14 overview URL with v15 across installation, compositor commands and theme configuration; verify the effect actually loads after installation.
- [x] Verify both Meta keys dispatch launcher actions, Meta+Tab opens overview without triggering Launcher on release, and Escape closes overview using a temporary uinput keyboard.
- [!] Physical keyboard acceptance remains user verification; injected input validates the native compositor route.

### Launcher timing and simultaneous auto-hide chrome (2026-10-11)

- [x] Use the shared 220ms shell entrance and fast-start easing for Launcher, retaining its full-height opaque slide and first-frame preparation.
- [x] Reveal auto-hidden bars independently from their own bottom-edge regions. Route input through the native panel as well as the sensor so a visible bar cannot block the other reveal. Keep separate hide timers and preserve fullscreen suppression.
- [x] Seven focused motion checks passed; native applications rebuilt and shell reloaded successfully. Strict compositor health: zero failures and warnings. Physical display acceptance remains pending.

- [x] Calendar weekday headers use `ContentSecondaryBrush` instead of the disabled text color for clearer labels (2026-10-11).

- [x] Fix accent swatch selected fills, separate the selection ring by 2px, and consolidate Default/Blue into one visible option while supporting saved Blue preferences. Light/dark checks passed; native builds and shell reload succeeded (2026-10-11).

- [x] Restore independent auto-hide reveals/timers and route bottom-edge input through the mapped native panel, so either hidden bar can be revealed while the other is visible. Seven focused tests passed; native builds/reload and strict compositor health checks passed (2026-10-11).

### Settings stability, swatch bounds and caption symbols (2026-10-11)

- [x] Reserve a full 32px swatch cell for the selection ring, keeping its 24px fill and 2px gap entirely inside the button bounds.
- [x] Remove 15-second full-page rebuilding, defer font enumeration until Appearance is opened, and decode wallpaper previews outside the UI thread.
- [x] Apply and verify persistent KWin Night Light state instead of a temporary preview; retain the toggle and page while applying changes.
- [x] Use an accent-independent white glyph for close-button hover/press across shared, Files, Calculator and Calendar titlebars.

Validation: six focused checks passed, persistent Night Light was verified on/off against live KWin beyond the preview timeout, native applications rebuilt and shell reloaded, and strict compositor health reported zero failures/warnings. Physical UI acceptance remains pending.

### Medium desktop media widget polish (2026-10-11)

- [x] Remove the medium spectrum and stop audio capture while only the medium layout is shown.
- [x] Lighten the artwork gradients, use the shared 14px title size, and match the large transport's glyph size, spacing and button styles.
- [x] Replace delayed scale swaps with a short fade and 6px vertical motion; reveal transport for keyboard focus as well as pointer hover using Reduce Motion-aware tokens.
- [x] Focused pointer/focus test passed with the two existing PickerEntry compile failures temporarily excluded through an external MSBuild target. Native applications rebuilt and shell reloaded; strict compositor health reports zero failures/warnings. The unmodified GUI test project remains blocked by OverallPolishTests and NotesEditorTests.
- [!] Physical artwork contrast and transition acceptance remain pending.

- [x] Medium media transport icons use the requested fixed `#F2F2F2`; medium and large buttons share `MediaBtn` hover colors, zero padding, `RoundedMD` radius, standard hit area and pressed feedback (2026-10-11).

- [x] Balance medium/large media transport with shared 8px gaps. Medium hover/press uses dark subtle-button white overlays (`#24FFFFFF` / `#33FFFFFF`) regardless of desktop theme (2026-10-11).

### Analog desktop clock (2026-10-11)

- [x] Add Analog Clock to the widget library, creation, saved-layout restoration and resizing; reuse the sweep clock's rounded rectangular dial with 12px primary hour/five-minute markers and 6px tertiary minute markers.
- [x] Render the supplied ClockWidget SVG hands around their ring centers: hour `(32, 31.9629)`, minute `(32, 32)`, second `(22, 75.9998)` read directly from the updated SVG circle. Use primary hour/minute hands and the theme accent for the continuously moving second hand.
- [x] Eight focused checks passed for pivot invariance, cardinal orientation and rendered light/dark clock/library layouts, including all categories at 640×560 and 1024×768. Inspected rendered clocks and library previews; `git diff --check` passed. The unmodified GUI test project remains blocked by existing PickerEntry compilation failures in OverallPolishTests and NotesEditorTests; focused checks used a temporary external MSBuild override.
- [!] Physical desktop animation acceptance remains pending.
- [x] Updated second hand reads its pivot `(22, 75.9998)` directly from the SVG circle; lengthened five-minute markers to 12px and changed other minute markers to `ContentTertiaryBrush`. Eight focused checks passed on the final combined run; inspected both theme renders. The initial library layout run failed intermittently, followed by passing standalone and combined reruns.

- [x] Increase analog clock five-minute markers from 12px to 16px, extending inward along the existing dial; other minute markers remain 6px (2026-10-11).

- [x] Explicitly antialias analog clock hand vector fills and ring strokes at their final rotation/scale, overriding inherited aliased rendering without rasterizing the SVG assets (2026-10-11).

### Daydate analog clock hands (2026-10-12)

- [x] Switch to the supplied daydate SVG hands; read hour/minute pivots `(30.5, 87)` and second pivot `(30.5, 116)` directly from their circles. Scale all hands together using the longest pivot-to-tip distance to preserve their proportions and dial reach.
- [x] Shell compiled and four focused pivot/orientation and light/dark rendering checks passed; inspected both theme renders and checked the diff. Focused tests used a temporary external override for the existing unrelated PickerEntry test compilation errors.
