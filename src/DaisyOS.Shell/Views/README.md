# DaisyOS

DaisyOS is an Arch Linux-based desktop OS experience with a custom shell built in C#, .NET, and Avalonia UI.

The project does not implement a kernel. It builds a controlled desktop environment on top of Arch Linux, starting with a fullscreen shell that can later be packaged into a bootable ISO.

## Current Status

This repository contains an Avalonia shell prototype:

- Repository structure for app source, OS build files, scripts, docs, and assets.
- A hand-coded Avalonia shell MVP with desktop, system bar, dock, launcher, settings, power, notifications, calendar, lock screen, and quick volume controls.
- Mock system services for development, with JSON-backed settings persistence.
- Real Linux wrappers for power, audio (PipeWire `wpctl`), NetworkManager, Bluetooth, battery, display, storage, CPU/memory, and GPU/Vulkan/gaming tool detection.
- Desktop `.desktop` app discovery and launching (real mode) or hybrid mock+real app registry.
- A Files app with persistent pinned locations, breadcrumb/direct-path navigation, list and grid views, search, copy/paste, trash, validated inline rename, hidden-file visibility, permission details, and bounded text previews.
- A read-only update checker that lists available Arch packages without synchronizing databases or installing updates.
- Wi-Fi connect/disconnect in Settings, Bluetooth toggle, live GPU/gaming status detection.
- Idle lock screen with Super+L shortcut, 5-minute idle timer.
- Self-contained Linux binary publishing (see `scripts/publish-shell.sh`).
- Safe development scripts that check dependencies before doing work.
- Documentation and AI agent safety guidelines.

The shell builds locally with .NET SDK 10. ISO/VM packaging is still a later milestone.

## Required Host Tools

- .NET SDK 10
- Git
- Arch `archiso` package, for `mkarchiso`
- QEMU, for `qemu-system-x86_64`
- ShellCheck
- VS Code, optional
- Avalonia templates, optional because this repo is hand-coded

Run:

```bash
scripts/setup-dev.sh
```

The script checks for tools and prints guidance. It does not install packages automatically.

## Run The Shell

After installing .NET SDK 10:

```bash
dotnet restore
dotnet build
dotnet run --project src/DaisyOS.Shell
```

The shell runs fullscreen and without normal window decorations. During development, press `Ctrl+Q` to quit and `Escape` to close overlays.

Welcome opens automatically only until onboarding has been completed. To reopen it while developing, use `--show-onboarding`. Tests and automation can explicitly bypass incomplete onboarding with `--skip-onboarding`:

```bash
dotnet run --project src/DaisyOS.Shell -- --show-onboarding
dotnet run --project src/DaisyOS.Shell -- --skip-onboarding
```

By default, the shell uses mock-safe service wiring for destructive actions. In mock mode, the launcher still discovers and loads real `.desktop` app entries from the system, so you can browse and launch installed applications. To opt into real Linux service wiring for local/ISO testing:

```bash
DAISYOS_SHELL_SERVICES=real dotnet run --project src/DaisyOS.Shell
dotnet run --project src/DaisyOS.Shell -- --real-services
```

## Publish

To run a complete Debug validation or create a versioned self-contained Linux x64 release:

```bash
scripts/build-debug.sh
scripts/build-release.sh
```

Release archives and SHA-256 checksums are written to `artifacts/release/`. The
published binary is also kept in `publish/linux-x64/` for ISO tooling compatibility:

```bash
DAISYOS_SHELL_SERVICES=mock ./publish/linux-x64/DaisyOS.Shell
```

Use `scripts/publish-shell.sh` when only the production unpacked publish tree is
needed. For quick local or nested-KWin testing, use
`scripts/publish-shell.sh --fast`; it keeps the same self-contained output path
but skips the expensive production ReadyToRun pass. Fast output must not be
copied into an ISO. See `docs/release.md` for version channels, artifact
verification, and the release checklist.

## Lock Screen

Press `Super+L` to lock the screen. The shell also locks automatically after 5 minutes of inactivity. Press Enter, Space, Escape, or click Unlock to return to the desktop.

## Settings

The shell persists user-facing settings such as theme, wallpaper, dock behavior, volume, and notification preferences to:

```text
~/.config/DaisyOS/settings.json
```

If `XDG_CONFIG_HOME` is set, DaisyOS writes to `$XDG_CONFIG_HOME/DaisyOS/settings.json` instead.

## Test

```bash
scripts/test.sh
scripts/format-check.sh
```

`format-check.sh` requires ShellCheck for shell script linting.

The automated suite covers settings persistence, safe command execution and cancellation, guarded async commands, desktop-entry parsing, Linux Wi-Fi/Bluetooth/update behavior, app policy, file-manager navigation, idle locking, startup options, and rendering calculations. Release-script checks cover metadata, naming, precedence, and checksums. See `docs/testing.md` for the current stabilization measurements and smoke checklist.

## Safety

Installer, partitioning, formatting, and real disk installation logic must not be implemented or run without explicit approval. VM testing comes first.
