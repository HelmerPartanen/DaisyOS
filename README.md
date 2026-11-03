# DaisyOS

<p align="center">
  <img width="1920" height="1080" alt="DaisyOS Desktop Preview" src="https://github.com/user-attachments/assets/2e5c54f0-1579-4417-bed3-29d772725dff" />
</p>

<p align="center">
  <a href="#daisyos-continuity-universal-device-experience"><img src="https://img.shields.io/badge/Continuity-Peer--to--Peer%20Convergence-blue?style=flat-square" alt="Continuity" /></a>
  <a href="https://dotnet.microsoft.com/"><img src="https://img.shields.io/badge/.NET-10.0-purple?style=flat-square" alt=".NET 10" /></a>
  <a href="https://avaloniaui.net/"><img src="https://img.shields.io/badge/UI-Avalonia%20XAML-indigo?style=flat-square" alt="Avalonia UI" /></a>
  <a href="https://wayland.freedesktop.org/"><img src="https://img.shields.io/badge/Display-Wayland%20%28Layer--Shell%29-orange?style=flat-square" alt="Wayland" /></a>
  <a href="https://archlinux.org/"><img src="https://img.shields.io/badge/Base-Arch%20Linux-1793D1?style=flat-square&logo=arch-linux&logoColor=white" alt="Arch Linux" /></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/License-MIT-green?style=flat-square" alt="MIT License" /></a>
</p>

---

**DaisyOS** is an Arch Linux-based desktop operating system powered by a custom **C# / .NET 10** and **Avalonia** shell, built natively for **Wayland** and managed by **KWin 6**. Designed to combine UNIX power with desktop ergonomics, DaisyOS delivers a cohesive, responsive experience featuring peer-to-peer multi-device convergence, interactive desktop widgets, fluid motion physics, and a suite of first-party native applications.

---

## 🌟 Flagship Feature: DaisyOS Continuity

**DaisyOS Continuity** breaks down the barrier between separate computers. Connect your DaisyOS desktop (Ethernet) and laptop (Wi-Fi) across the same local network to create a seamless, unified workspace—**without cloud accounts, internet connectivity, or external servers**.

```
DESKTOP (Ethernet)                                 LAPTOP (Wi-Fi)
┌─────────────────────────────────┐               ┌─────────────────────────────────┐
│ Settings, Files, Notes, Shell   │               │ Settings, Files, Notes, Shell   │
│ Portals (InputCapture, Remote)  │               │ Portals (InputCapture, Remote)  │
│               │ D-Bus IPC       │               │               │ D-Bus IPC       │
│ continuityd (user daemon)       │◄── TLS 1.3 ──►│ continuityd (user daemon)       │
│ • Discovery, Pairing & Trust    │   Local LAN   │ • Discovery, Pairing & Trust    │
│ • Input, Clipboard, Audio, File │               │ • Input, Clipboard, Audio, File │
└─────────────────────────────────┘               └─────────────────────────────────┘
                 ▲                                                 ▲
                 └────────────── mDNS / DNS-SD Discovery ──────────┘
```

### Key Continuity Capabilities

- 🖱️ **Universal Control (Seamless Screen-Edge Input)**:
  - Move your mouse pointer seamlessly across screen edges (**Left**, **Right**, **Above**, or **Below**) to navigate directly between separate computers.
  - Keyboard focus automatically follows the mouse cursor with zero manual switching.
  - Operates popup-free after a one-time pairing using Wayland XDG Desktop Portals (`InputCapture`, `RemoteDesktop`) and native `libei`/`libeis` input emulation.
  - Features instantaneous local emergency recovery via `Ctrl+Alt+Shift+Escape`, anti-echo loop prevention, and automatic re-establishment upon wake.

- 📋 **Universal Clipboard**:
  - Instant, bi-directional text synchronization between trusted machines on your LAN.
  - Loop prevention safeguards, private local memory buffers, and automatic isolation when either workstation locks.

- 📁 **Consented High-Speed File Transfer**:
  - Directly send files to paired computers via the context menu in **DaisyOS Files**.
  - Fast, chunked peer-to-peer streams with SHA-256 verification, destination path sandboxing, and progress feedback.

- 🔊 **Wireless Audio Routing**:
  - Route system audio playback to another computer's speakers with a single click in **Quick Settings → Audio outputs**.
  - Powered by PipeWire/PulseAudio null-sink streams transmitting 48 kHz stereo s16le PCM over encrypted TLS, with automatic stream recovery and bidirectional loop protection.

- 📝 **App Handoff**:
  - Pick up right where you left off. Right-click a document in **DaisyOS Notes** and choose **Continue on another computer…** to instantly transfer versioned activity snapshots to your second workstation.

- 🖥️ **Screen Viewing & Remote Desktop**:
  - Low-latency screen mirroring and remote control sessions over authenticated TLS streams for ad-hoc assistance or secondary monitoring.

- 🔒 **Zero-Cloud, LAN-First Cryptographic Security**:
  - Decentralized local mDNS/DNS-SD discovery via Avahi (`_daisycont._tcp.local.`) with manual IP fallback.
  - Mutual TLS 1.3 authentication backed by Ed25519 cryptographic keypairs and full fingerprint pinning verified on both physical displays during pairing.
  - Strict least-privilege capability permissions enforced at both daemon and Wayland portal boundaries; fails closed when locked or revoked.

> 📖 **Deep Dive**: Explore the full Continuity engineering specifications in [docs/continuity/MASTER_PLAN.md](docs/continuity/MASTER_PLAN.md), the user guide in [docs/continuity/SEAMLESS_CONTROL.md](docs/continuity/SEAMLESS_CONTROL.md), and the cryptographic security model in [docs/continuity/SECURITY.md](docs/continuity/SECURITY.md).

---

## 🖥️ Shell & User Experience

<p align="center">
  <img width="1920" height="1080" alt="DaisyOS Shell and Applications" src="https://github.com/user-attachments/assets/d7700922-30a6-4a7e-b664-7816c2ed36eb" />
</p>

### Pure Wayland Layer-Shell Architecture
The shell runs as a pure Wayland client leveraging the `wlr-layer-shell` protocol, hosting the desktop workspace grid, widgets, taskbar/dock, notification center, and system status bar. KWin 6 orchestrates window management, blur effects, and compositor-level security boundaries.

### Interactive Desktop Widgets
- **Clock**: Multiple interchangeable visual presentations including Sweep, Digital, Analog, and Glow styles.
- **Media Playback**: Live media controls with real-time PipeWire audio spectrum analysis.
- **Calendar & Photos**: Interactive calendar overview and photo gallery frames.
- **3D Widget Gallery**: Carousel widget browser featuring 3D ambient floating perspective tilt and smooth multi-touch trackpad gesture navigation.

### Design System & Fluid Motion (`GuiMotion`)
- **Semantic Design Tokens**: Strict token contract system (`Theme_Tokens.md`) ensuring WCAG AA contrast compliance across unified **Light** and **Dark** modes.
- **Physics-Based Animation**: Natural spring motion and fluid curves applied across launcher transitions, window snap previews, and overlay drawers.
- **Native C++ KWin Integrations**: Custom window decoration plugins, frame blur effects, and busy-cursor tracking located in `src/native/`.

---

## 📦 Native Applications Suite

DaisyOS includes a suite of first-party applications built with modern .NET 10 and Avalonia XAML, adhering to the system design language:

| Application | Assembly | Description |
| :--- | :--- | :--- |
| **Files** | `DaisyOS.Files` | Clean dual-pane file manager with Quick Look previews, archive handling, and native Continuity sharing. |
| **Settings** | `DaisyOS.Settings` | Centralized control center for displays, scaling, audio sinks, Wi-Fi, Bluetooth, themes, and Continuity pairing. |
| **Mail** | `DaisyOS.Mail` | Native desktop email client with fast indexing, modern typography, and multi-account support. |
| **Notes** | `DaisyOS.Notes` | Distraction-free note editor featuring instant cross-computer Continuity handoff. |
| **Disk Manager** | `DaisyOS.DiskManager` | Storage overview, drive partitioning, SMART diagnostics, and filesystem maintenance. |
| **App Installer** | `DaisyOS.AppInstaller` | Streamlined graphical installer for standalone AppImage and Arch package payloads. |
| **Installer** | `DaisyOS.Installer` | Elegant, step-by-step operating system installation wizard for hardware targets. |

---

## 🏗️ System Architecture

```
┌────────────────────────────────────────────────────────────────────────┐
│                        First-Party Native Apps                         │
│       Files  •  Settings  •  Mail  •  Notes  •  DiskManager            │
├────────────────────────────────────────────────────────────────────────┤
│                     Avalonia Desktop Shell (Layer-Shell)               │
│      Desktop Grid  •  Widgets  •  Dock  •  Launcher  •  Notifications   │
├──────────────────────────────────────┬─────────────────────────────────┤
│            DaisyOS.Core              │          DaisyOS.System         │
│  Contracts, Token System, Storage    │  Hardware, Display, Audio, BlueZ│
├──────────────────────────────────────┴─────────────────────────────────┤
│                          Background Daemons                            │
│    daisyos-continuity (TLS 1.3)     │      daisy-launch (IPC)          │
├─────────────────────────────────────┴──────────────────────────────────┤
│                  KWin 6.x Compositor & Native Plugins                  │
│       Wayland Protocols  •  XDG Portals  •  libei  •  C++ Plugins      │
├────────────────────────────────────────────────────────────────────────┤
│                       Linux Kernel & Arch Linux Base                   │
│          PipeWire  •  PulseAudio  •  NetworkManager  •  systemd        │
└────────────────────────────────────────────────────────────────────────┘
```

---

## 🚀 Quick Start for Contributors

### Prerequisites

- **Operating System**: Linux (Arch Linux recommended; Fedora and Ubuntu supported for development).
- **.NET SDK**: .NET 10.0+ (`dotnet --version`).
- **Native Toolchain**: `gcc` or `clang`, `cmake` (3.25+), `ninja` or `make`, `pkg-config`.
- **System Libraries**: `wayland-client`, `wayland-protocols`, `kwin` dev headers (KWin 6.x), `libpulse`, `bluez`, `libei`, `gstreamer`, `polkit-agent-1`.

### Build & Run

Clone the repository and build the primary solution:

```bash
# Clone the repository
git clone https://github.com/helmeri/DaisyOS.git
cd DaisyOS

# Restore and compile solution
dotnet restore
dotnet build
```

#### Running the Desktop Shell

Run the shell locally in mock service mode (safe for non-Arch / workstation environments):
```bash
dotnet run --project src/DaisyOS.Shell
```

Run against live system hardware services (PipeWire/PulseAudio, BlueZ, NetworkManager):
```bash
DAISYOS_SHELL_SERVICES=real dotnet run --project src/DaisyOS.Shell
```

#### Testing in a Nested KWin Wayland Session

Test the full compositor and shell safely inside an isolated nested Wayland window without disrupting your active desktop:
```bash
scripts/publish-shell.sh --fast
scripts/run-kwin-prototype.sh
```

#### Building and Running Continuity Daemon

Compile and install the Continuity daemon and native media helpers:
```bash
scripts/build-continuity.sh
scripts/install-continuity.sh
systemctl --user daemon-reload
systemctl --user restart daisyos-continuity.service
```

#### Native Development Session (Hot Reloading)

When working inside the dedicated `daisy-native-development` Wayland session:
```bash
# Rebuilds the shell and standalone apps, cleanly replacing the layer-shell process:
daisyos-native-development restart

# Inspect memory policy and consumption:
daisyos-native-development memory status
```

---

## 🧪 Testing & Quality Assurance

DaisyOS enforces strict design token contracts, architecture boundaries, and headless UI verification:

```bash
# Run design token and core contract test suites
dotnet test tests/DaisyOS.Tests/DaisyOS.Tests.csproj

# Run headless shell UI and widget layout test suites
dotnet test tests/DaisyOS.ShellDemo.Tests/DaisyOS.ShellDemo.Tests.csproj

# Run shell component and service tests
dotnet test tests/DaisyOS.Shell.Tests/DaisyOS.Shell.Tests.csproj

# Run the complete automated release gate and reliability suite
scripts/test.sh
```

> ⚠️ **Design Token Notice**: All XAML views must reference semantic design tokens (`{DynamicResource TypeSize...}`, `{DynamicResource IconSize...}`). Hardcoded numeric font sizes or icon dimensions will fail automated `DesignTokenContractTests`.

---

## 📁 Repository Structure

```
├── docs/                     # Architectural specifications, audits, and guides
│   ├── apps/                 # Application design and user experience specs
│   ├── architecture/         # Layer specifications, typography, and motion contracts
│   ├── continuity/           # Continuity protocols, master plan, and security model
│   └── validation/           # Quality audits, benchmarks, and regression logs
├── os/                       # ArchISO profiles, systemd service units, and OS configs
├── packaging/                # Arch PKGBUILDs, KWin decoration scripts, portal configs
├── scripts/                  # Build, test, packaging, and developer helper scripts
├── services/                 # Native system service helpers (DaisyLaunch)
├── src/
│   ├── DaisyOS.Shell/        # Primary Wayland desktop shell, overlays, and widgets
│   ├── DaisyOS.System/       # Linux hardware integrations (Audio, Bluetooth, Displays, Gaming)
│   ├── DaisyOS.Core/         # Shared models, contracts, and storage primitives
│   ├── DaisyOS.Continuity/   # Device continuity service, transport, and pairing logic
│   ├── DaisyOS.Files/        # Files file-manager application
│   ├── DaisyOS.Settings/     # System settings control center
│   ├── DaisyOS.Mail/         # Mail client application
│   ├── DaisyOS.Notes/        # Notes application with Continuity handoff
│   ├── DaisyOS.DiskManager/  # Disk storage and partition manager
│   ├── DaisyOS.AppInstaller/ # AppImage and package installer
│   ├── DaisyOS.Installer/    # Operating system installation wizard
│   └── native/               # Native C++ plugins (KWin frame effects, portal capture helpers)
└── tests/
    ├── DaisyOS.Tests/            # Design tokens, contracts, and backend unit tests
    ├── DaisyOS.ShellDemo.Tests/  # Headless Avalonia UI, widget, and layout tests
    └── DaisyOS.Shell.Tests/      # Shell service and interaction tests
```

---

## 📚 Key Documentation

- 🔗 **Continuity**: [Master Plan](docs/continuity/MASTER_PLAN.md) • [Seamless Control Guide](docs/continuity/SEAMLESS_CONTROL.md) • [Security & Cryptography](docs/continuity/SECURITY.md) • [Wire Protocol](docs/continuity/PROTOCOL.md)
- 🔗 **Architecture & Compositor**: [System Architecture](docs/architecture.md) • [Compositor Integration](docs/compositor.md) • [Window Snapping](docs/window-snapping.md)
- 🔗 **Design & Polish**: [Design Tokens Contract](docs/Theme_Tokens.md) • [Motion Physics](docs/gui-motion.md) • [Keyboard Shortcuts](docs/keyboard-shortcuts.md)
- 🔗 **Performance & Reliability**: [Reliability Audit](docs/everyday-reliability.md) • [Performance Benchmarks](docs/performance-benchmark.md)

---

## 🤝 Contributing

Contributions are warmly welcomed! Please read [CONTRIBUTING.md](CONTRIBUTING.md) for code formatting rules, pull request workflows, and engineering guidelines before submitting patches.

---

## 📄 License

DaisyOS is open-source software licensed under the [MIT License](LICENSE).
