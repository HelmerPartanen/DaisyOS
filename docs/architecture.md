# Architecture

DaisyOS is built in two layers:

1. Arch Linux base: kernel, systemd, graphics stack, networking, audio, packages, and ISO generation.
2. DaisyOS shell: a C#/.NET/Avalonia UI that provides the desktop, launcher, settings, notifications, and system controls.

The first milestone is a bootable Arch-based ISO that starts the Avalonia shell automatically.

## Current Implementation

- `DaisyOS.Core` contains shared models, service interfaces, and small helpers.
- `DaisyOS.System` contains Linux-oriented service implementations and safe process execution helpers.
- `DaisyOS.Shell` contains the Avalonia app, views, viewmodels, theme resources, and mock service wiring.

The shell currently uses mock services by default so it can run safely on a developer workstation.

