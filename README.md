# DaisyOS

DaisyOS is an Arch Linux-based desktop operating system with a custom C#/.NET 10 and Avalonia shell, tailored for Wayland and managed by KWin. It delivers a cohesive, responsive experience featuring a desktop grid with interactive widgets, unified bottom chrome, application launcher, notification center, Lock Screen with media integration, system settings, native files app, and first-party applications.

---

## Highlights & Features

- **Custom Avalonia Desktop Shell**: A pure Wayland layer-shell experience hosting the desktop grid, widgets, taskbar/dock, and system status bar.
- **Interactive Widgets**: Interactive desktop widgets for Clock (Sweep, Digital, Analog, Glow), Calendar, Media playback with audio spectrum analyzer, and Photos. Includes an Apple-inspired carousel Widget Gallery featuring 3D ambient floating perspective tilt and trackpad gesture swiping.
- **Native Applications Suite**:
  - **Files (`DaisyOS.Files`)**: Clean dual-pane file management with Quick Look integration.
  - **Settings (`DaisyOS.Settings`)**: Unified system controls for displays, audio, networks, Bluetooth, appearance, and accessibility.
  - **Mail (`DaisyOS.Mail`)**, **Notes (`DaisyOS.Notes`)**, **Disk Manager (`DaisyOS.DiskManager`)**, **App Installer (`DaisyOS.AppInstaller`)**, and **Installer (`DaisyOS.Installer`)**.
  - **DaisyOS Continuity (`DaisyOS.Continuity`)**: Multi-device pairing and synchronization service.
- **Dual-Theme Semantic Design System**: Full Light and Dark themes backed by design token contracts, semantic color tokens, WCAG AA compliance, and fluid motion physics (`GuiMotion`).
- **Compositor Integration**: Native C++ KWin decoration plugins, blur mask integration, and custom effects in `src/native/`.

---

## Quick Start for Contributors

### Prerequisites

- **OS**: Linux (Arch Linux recommended; Fedora / Ubuntu supported for development).
- **.NET SDK**: .NET 10.0+ (`dotnet --version`).
- **Native Toolchain**: `gcc` / `clang`, `cmake` (3.25+), `ninja` or `make`, `pkg-config`.
- **System Libraries**: `wayland-client`, `wayland-protocols`, `kwin` dev headers (KWin 6.x), `libpulse`, `bluez`, `polkit-agent-1`.

### Build & Run

Clone the repository and build the primary solution:

```bash
# Clone with submodules if any
git clone https://github.com/helmeri/DaisyOS.git
cd DaisyOS

# Restore and build the solution
dotnet restore
dotnet build
```

#### Running the Shell

To run the shell locally in mock service mode:
```bash
dotnet run --project src/DaisyOS.Shell
```

To run against real system services (PulseAudio/PipeWire, BlueZ, NetworkManager):
```bash
DAISYOS_SHELL_SERVICES=real dotnet run --project src/DaisyOS.Shell
```

#### Testing in a Nested KWin Compositor

You can run the full shell safely inside a nested Wayland window without altering your desktop session:
```bash
scripts/publish-shell.sh --fast
scripts/run-kwin-prototype.sh
```

#### Native Development Session (Hot Reloading)

When logged into the dedicated `daisy-native-development` Wayland session:
```bash
# Rebuilds the shell and standalone apps and replaces only the layer-shell process:
daisyos-native-development restart

# Check memory status and protected memory limits:
daisyos-native-development memory status
```

---

## Running Tests

DaisyOS enforces strict design tokens, architecture contracts, and headless UI tests.

```bash
# Run design token and core contract tests
dotnet test tests/DaisyOS.Tests/DaisyOS.Tests.csproj

# Run headless shell UI and widget layout tests
dotnet test tests/DaisyOS.ShellDemo.Tests/DaisyOS.ShellDemo.Tests.csproj

# Run shell component tests
dotnet test tests/DaisyOS.Shell.Tests/DaisyOS.Shell.Tests.csproj

# Run all automated test suites
scripts/test.sh
```

> **Design Token Notice**: XAML views must use token references (`{DynamicResource TypeSize...}`, `{DynamicResource IconSize...}`). Hardcoded numeric font sizes or icon sizes will fail `DesignTokenContractTests`.

---

## Repository Structure

```
├── docs/                     # Architecture, specifications, keyboard shortcuts, and audits
├── os/                       # ArchISO profiles, systemd service units, and OS configs
├── scripts/                  # Build, test, packaging, and developer helper scripts
├── src/
│   ├── DaisyOS.Shell/        # Primary desktop shell, window overlay managers, and widgets
│   ├── DaisyOS.System/       # Linux hardware & service integrations (Audio, Bluetooth, Network, Displays)
│   ├── DaisyOS.Core/         # Shared models, contracts, and storage primitives
│   ├── DaisyOS.Files/        # Files file-manager application
│   ├── DaisyOS.Settings/     # System settings application
│   ├── DaisyOS.Mail/         # Mail client application
│   ├── DaisyOS.Notes/        # Notes application
│   ├── DaisyOS.DiskManager/  # Disk storage and partition manager
│   ├── DaisyOS.AppInstaller/ # AppImage / package installer application
│   ├── DaisyOS.Installer/    # System installation wizard
│   ├── DaisyOS.Continuity/   # Device continuity service and pairing dialogs
│   └── native/               # KWin C++ plugins (frame effect, window menus, busy cursor)
└── tests/
    ├── DaisyOS.Tests/            # Design tokens, contracts, and backend unit tests
    ├── DaisyOS.ShellDemo.Tests/  # Headless Avalonia UI, widget library, and layout tests
    └── DaisyOS.Shell.Tests/      # Shell service and interaction tests
```

---

## Contributing Guidelines

Please see [CONTRIBUTING.md](CONTRIBUTING.md) for code formatting, pull request workflows, and development guidelines.

---

## License

DaisyOS is open-source software licensed under the [MIT License](LICENSE).
