<img width="1920" height="1080" alt="Screenshot 2026-11-08 22 00 34 567-8247bbe942ee4b7d98cbf6bed9667d0a" src="https://github.com/user-attachments/assets/ab7c52c2-341a-4f97-9d98-f91b52765fa2" />


# DaisyOS

DaisyOS is an Arch Linux-based desktop operating system built around a custom C# / .NET 10 and Avalonia shell, managed by KWin on Wayland. It pairs the flexibility of Arch Linux with a dedicated native shell, desktop widgets, a core set of first-party applications, and built-in cross-device Continuity.

---

## Continuity

Continuity is DaisyOS's built-in multi-device integration service (`DaisyOS.Continuity`). It connects DaisyOS machines (such as a desktop on Ethernet and a laptop on Wi-Fi) across the same local network without cloud services, third-party accounts, or internet access.

After pairing two devices once in Settings, Continuity handles device coordination directly over mutual TLS 1.3:

- **Seamless Input (Edge Crossing)**: Move your mouse past any screen edge (left, right, above, or below) to slide control over to the adjacent computer. Keyboard focus follows the pointer. It uses Wayland's `InputCapture` and `RemoteDesktop` portals alongside `libei` / `libeis` for direct input injection, requiring no virtual display or viewer window. An emergency escape shortcut (`Ctrl+Alt+Shift+Escape`) immediately returns pointer focus to the local machine.
- **Shared Clipboard**: Copies on one machine become available on the other over encrypted LAN channels. Clipboard sync is scoped, ignores password manager secrets where marked, does not persist clipboard history, and stops immediately when either machine locks.
- **Direct File Transfers**: Send files directly from `DaisyOS.Files` to any paired machine on the network. Transfers stream over authenticated TCP with SHA-256 validation and safe destination path sandboxing.
- **Audio Routing**: Switch audio output to a paired computer's speakers directly from the Quick Settings menu. The sender captures local audio through a PipeWire / PulseAudio null sink and streams raw 48 kHz stereo PCM over TLS to the receiver.
- **App Handoff**: Continue work on another machine. For example, right-clicking a document in `DaisyOS.Notes` sends a versioned snapshot directly to the paired computer.
- **Screen Viewing**: On-demand screen mirroring and remote control sessions through standard desktop portals for remote assistance or monitoring.
- **Local Security & Discovery**: Devices discover each other using mDNS / DNS-SD via Avahi (`_daisycont._tcp.local.`), with manual IP fallback. Connections require mutual TLS 1.3 with full Ed25519 certificate fingerprint verification during pairing. All capabilities fail closed upon session lock, suspension, or credential revocation.

For protocol specifications, wire format definitions, and security threat models, see:
- [Continuity Master Plan](docs/continuity/MASTER_PLAN.md)
- [Seamless Input Guide](docs/continuity/SEAMLESS_CONTROL.md)
- [Security Architecture](docs/continuity/SECURITY.md)
- [Wire Protocol & Transport](docs/continuity/PROTOCOL.md)

---

## Desktop Shell & Design

<img width="1920" height="1080" alt="Screenshot 2026-11-08 22 01 28 425-cae7707a1b264f7293e454cf992c0de7" src="https://github.com/user-attachments/assets/7435805b-b6f8-47b2-82a2-13e030ad6330" />

The shell (`src/DaisyOS.Shell`) is implemented in Avalonia and runs as a Wayland layer-shell surface managed by KWin 6.

- **Wayland Integration**: Custom layer-shell surfaces handle the desktop workspace grid, widgets, bottom dock, application launcher, notifications, and lock screen.
- **Interactive Widgets**: Desktop widgets include customizable clocks (sweep, digital, analog, glow), an audio spectrum analyzer media widget backed by PipeWire, calendar, and photo frames. A 3D perspective carousel widget gallery supports trackpad swipe gestures.
- **KWin Plugins**: Native C++ plugins in `src/native/` supply window decorations, frame blur effects, and window snapping helpers.
- **Design System & Motion**: Strict semantic token contracts (`docs/Theme_Tokens.md`) govern typography, spacing, and colors across Light and Dark themes, paired with custom spring motion physics (`docs/gui-motion.md`).

---

## First-Party Applications

DaisyOS provides a suite of native applications built with Avalonia and .NET 10:

- **Files (`DaisyOS.Files`)**: Dual-pane file manager with Quick Look previews and built-in Continuity sending.
- **Settings (`DaisyOS.Settings`)**: System configuration center for displays, audio devices, networking, Bluetooth, appearance, and Continuity pairing.
- **Mail (`DaisyOS.Mail`)**: Desktop email client with multi-account support and clean typography.
- **Notes (`DaisyOS.Notes`)**: Fast note-taking app with instant cross-device Continuity handoff.
- **Disk Manager (`DaisyOS.DiskManager`)**: Partition management, disk health diagnostics, and storage overview.
- **App Installer (`DaisyOS.AppInstaller`)**: Graphical package and AppImage installer.
- **Installer (`DaisyOS.Installer`)**: Operating system installation wizard for bare-metal installs.

---

## Architecture

```
+-----------------------------------------------------------------------+
|                       First-Party Applications                        |
|        Files   |   Settings   |   Mail   |   Notes   |  DiskManager   |
+-----------------------------------------------------------------------+
|                     Avalonia Desktop Shell (Layer-Shell)              |
|        Grid    |    Widgets   |   Dock   |  Launcher | Notifications  |
+-----------------------------------+-----------------------------------+
|            DaisyOS.Core           |           DaisyOS.System          |
|    Tokens, Storage, Contracts     | Hardware, Displays, Audio, BlueZ  |
+-----------------------------------+-----------------------------------+
|                          System Services                              |
|     daisyos-continuity (TLS 1.3)  |         daisy-launch (IPC)        |
+-----------------------------------------------------------------------+
|                 KWin 6.x Compositor & Native Helpers                  |
|        Wayland Layer-Shell  |  XDG Portals  |  libei  |  C++ Plugins  |
+-----------------------------------------------------------------------+
|                     Linux Kernel & Arch Linux Base                    |
|           PipeWire  |  PulseAudio  |  NetworkManager  |  systemd      |
+-----------------------------------------------------------------------+
```

---

## Rust + Slint migration

An incremental native UI workspace is now under `crates/` and `apps/`.
`bash scripts/build-rust-ui.sh` builds the DaisyUI gallery and Calculator migration
slice. The current desktop and app launch routes remain available while parity
is verified; this is not a completed shell replacement. See the
[migration inventory, decisions and cutover gates](docs/migration/RUST_SLINT_MIGRATION.md).

---

## Developing Background Features in Rust

DaisyOS prioritizes **Rust for new background services, system daemons, and OS brokers**. If you are planning or implementing a new background feature, please follow these conventions before writing managed (.NET/C#) or heavy C++ code.

### Why Rust for Background Features?

Background processes (request brokers, hardware watchers, idle monitors, portal backends, and session daemons) run 24/7 across the desktop lifecycle:

- **Strict Resident Memory Budget (PSS):** Managed runtimes (.NET/Avalonia) and full GUI toolkits (Qt/KDE) carry large idle memory footprints (often 15–50+ MiB PSS). In contrast, resident Rust daemons idle at **~1–2 MiB PSS or less** (for example, migrating `DaisyLaunch` to a resident Rust broker reduced idle PSS by 88% from 13.3 MiB to 1.6 MiB, and the `daisy_idle` monitor idles at ~180 KiB PSS).
- **Predictable Latency & Zero GC:** Background monitors and IPC brokers must respond instantly without garbage collection pauses, memory leaks, or erratic scheduling.
- **System Safety & Reliability:** Rust's memory safety guarantees and explicit error handling prevent silent background daemon crashes that compromise desktop session stability.

### Core Architectural Pattern: "Resident Broker + On-Demand UI Helper"

To keep the system lightweight and responsive, avoid keeping GUI toolkits permanently resident in the background:

1. **Headless Resident Broker (Rust):** The persistent background daemon runs as a lean, headless Rust process. It handles the event loop, watches hardware/system events, manages state, and exposes D-Bus endpoints (`zbus`). It never loads a graphical toolkit.
2. **On-Demand UI Helper:** When interactive user feedback is required (such as a consent dialog, password prompt, file picker, or notification window), the Rust broker spawns or leases an ephemeral UI worker (Avalonia or Slint) on-demand. When the interaction finishes, the UI process exits or enters a dormant state, freeing all GUI assets from memory.

### What Qualifies as a "Background Feature"?

- **D-Bus Service Brokers:** Session and system bus endpoints (`org.daisyos.*`, `org.freedesktop.*`) that coordinate apps or desktop operations (e.g., `services/DaisyLaunch/rust`).
- **Hardware & State Monitors:** Daemons observing Wayland protocols, power/battery events, network changes, audio routing, or user idle time (e.g., `src/native/daisy_background`).
- **Desktop Portals & Permission Brokers:** Headless handlers for XDG desktop portals (ScreenCast, RemoteDesktop, InputCapture, GlobalShortcuts, Notifications, Polkit) like `src/native/daisy_desktop_services`.
- **Background Tasks & Synchronizers:** LAN discovery, device pairing coordination, or background file transfer workers.

### Code Organization

Place new background implementations according to their scope:

- **Standalone Daemons & Session Services:** `services/<ServiceName>/rust/` (e.g. [`services/DaisyLaunch/rust`](services/DaisyLaunch/rust))
- **Native OS Brokers & C-ABI Libraries:** `src/native/<service_name>/` (e.g. [`src/native/daisy_desktop_services`](src/native/daisy_desktop_services), [`src/native/daisy_background`](src/native/daisy_background))
- **Shared Workspace Crates:** `crates/` (e.g. `crates/daisy-platform`, `crates/daisy-core`)

### Recommended Stack & Guidelines

- **D-Bus Communication:** Use [`zbus`](https://crates.io/crates/zbus) (with `tokio`) for defining D-Bus interfaces, methods, and signal emitters.
- **Serialization:** Use `serde` and `serde_json` for structured wire payloads and IPC protocols.
- **Wayland & System APIs:** Link against standard system shared libraries (`libc`, `wayland-client`) using minimal FFI bindings rather than heavyweight standalone runtimes.
- **Shell / C# Interoperability:** Interface with the Avalonia shell through asynchronous D-Bus methods/signals (via `Tmds.DBus` on the C# side) or via a clean C ABI (`cdylib` with `extern "C"` functions, explicit ownership boundaries, and bounded thread stacks).
- **Dependency & Memory Hygiene:** Keep dependency trees minimal. Avoid pulling large crates into resident background services, and configure release builds with `opt-level = "z"` or `"s"`, `lto = true`, `panic = "abort"`, and `strip = true`.

### Existing Reference Implementations

- [`services/DaisyLaunch/rust`](services/DaisyLaunch/rust): Resident D-Bus request broker (`org.daisyos.Launcher1`) with worker leasing.
- [`src/native/daisy_desktop_services`](src/native/daisy_desktop_services): Resident portal broker for notifications, session capture, and Polkit integration.
- [`src/native/daisy_background`](src/native/daisy_background): High-efficiency Wayland idle monitor (`libdaisy_idle.so` C ABI and standalone daemon).
- [docs/performance/background-rust-migration.md](docs/performance/background-rust-migration.md): Background process RAM audit and `no_std` / FFI design principles.
- [docs/performance/launcher-rust-migration.md](docs/performance/launcher-rust-migration.md): Resident broker + on-demand helper architecture and memory audit.

---

## Contributor Guide

### Prerequisites

- **OS**: Linux (Arch Linux recommended; Fedora and Ubuntu work for development).
- **.NET SDK**: 10.0+ (`dotnet --version`).
- **Rust Toolchain**: 1.82+ / stable (`rustc --version`, `cargo --version`).
- **C/C++ Toolchain**: `gcc` or `clang`, `cmake` (3.25+), `ninja` or `make`, `pkg-config`.
- **System Libraries**: `wayland-client`, `wayland-protocols`, `kwin` dev headers (KWin 6.x), `libpulse`, `bluez`, `libei`, `gstreamer`, `polkit-agent-1`.

### Build

```bash
git clone https://github.com/HelmerPartanen/DaisyOS
cd DaisyOS

dotnet restore
dotnet build
```

### Running the Shell

To run the shell in mock service mode (does not require root or live hardware services):
```bash
dotnet run --project src/DaisyOS.Shell
```

To run against real system services (PulseAudio/PipeWire, BlueZ, NetworkManager):
```bash
DAISYOS_SHELL_SERVICES=real dotnet run --project src/DaisyOS.Shell
```

To run inside an isolated nested KWin Wayland session:
```bash
scripts/publish-shell.sh --fast
scripts/run-kwin-prototype.sh
```

### Building Continuity

To compile and install the Continuity service and native helpers:
```bash
scripts/build-continuity.sh
scripts/install-continuity.sh
systemctl --user daemon-reload
systemctl --user restart daisyos-continuity.service
```

### Native Development Session (Hot Reloading)

When running inside the `daisy-native-development` session:
```bash
# Rebuild the shell and standalone apps and restart the layer-shell process:
daisyos-native-development restart

# Check memory status and policy constraints:
daisyos-native-development memory status
```

---

## Testing

DaisyOS includes automated tests for design token contracts, backend logic, headless Avalonia UI components, and Rust native services:

```bash
# Run design token and core contract tests
dotnet test tests/DaisyOS.Tests/DaisyOS.Tests.csproj

# Run headless shell UI and widget layout tests
dotnet test tests/DaisyOS.ShellDemo.Tests/DaisyOS.ShellDemo.Tests.csproj

# Run shell component and service tests
dotnet test tests/DaisyOS.Shell.Tests/DaisyOS.Shell.Tests.csproj

# Run Rust unit and workspace tests
cargo test --workspace

# Lint Rust background services and native brokers
cargo clippy --manifest-path services/DaisyLaunch/rust/Cargo.toml -- -D warnings
cargo clippy --manifest-path src/native/daisy_desktop_services/Cargo.toml -- -D warnings
cargo clippy --manifest-path src/native/daisy_background/Cargo.toml -- -D warnings

# Run the full automated test suite
scripts/test.sh
```

Note: All XAML views must use token references (`{DynamicResource TypeSize...}`, `{DynamicResource IconSize...}`). Hardcoded numeric font or icon dimensions fail `DesignTokenContractTests`.

---

## Repository Structure

```
├── apps/                     # Rust Slint migration applications
├── crates/                   # Rust platform, core, and native UI workspace crates
├── docs/                     # Architecture specs, audits, and guides
│   ├── apps/                 # Application-specific documentation
│   ├── architecture/         # Layer specs, typography, and motion rules
│   ├── continuity/           # Continuity protocols, master plan, and security
│   └── validation/           # Benchmarks, audit notes, and regression logs
├── os/                       # ArchISO profiles, systemd service units, and OS configs
├── packaging/                # Arch PKGBUILDs, KWin decoration scripts, portal configs
├── scripts/                  # Build, test, and developer automation scripts
├── services/                 # Core OS daemons and resident services (DaisyLaunch Rust broker)
├── src/
│   ├── DaisyOS.Shell/        # Primary Wayland desktop shell and widgets
│   ├── DaisyOS.System/       # Linux hardware services (Audio, Network, Displays)
│   ├── DaisyOS.Core/         # Shared contracts, models, and storage primitives
│   ├── DaisyOS.Continuity/   # Continuity daemon, transport, and pairing logic
│   ├── DaisyOS.Files/        # Files application
│   ├── DaisyOS.Settings/     # Settings application
│   ├── DaisyOS.Mail/         # Mail application
│   ├── DaisyOS.Notes/        # Notes application
│   ├── DaisyOS.DiskManager/  # Disk partition and storage manager
│   ├── DaisyOS.AppInstaller/ # AppImage and package installer
│   ├── DaisyOS.Installer/    # System installation wizard
│   └── native/               # Native Rust brokers (portals, idle) and C/C++ KWin plugins
└── tests/
    ├── DaisyOS.Tests/            # Design tokens and backend unit tests
    ├── DaisyOS.ShellDemo.Tests/  # Headless Avalonia UI and widget tests
    └── DaisyOS.Shell.Tests/      # Shell service and interaction tests
```

---

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for code formatting guidelines, branch workflows, and PR requirements.

---

## License

DaisyOS is licensed under the [MIT License](LICENSE).

<img width="1920" height="1080" alt="image" src="https://github.com/user-attachments/assets/a16ff3fd-1de0-490b-9657-64eccb558736" />

<img width="1920" height="1080" alt="Screenshot 2026-11-08 22 01 12 041-3e0aaed511134c79b64a71ae838b9480" src="https://github.com/user-attachments/assets/f98b1616-bce2-45f6-b28c-994ccdaf718d" />

<img width="1920" height="1080" alt="Screenshot 2026-11-08 22 02 41 096-855b512019f74cc8be33bf517a2ba009" src="https://github.com/user-attachments/assets/f7dbef58-c76a-4b4f-9d50-6e56cbbc7e20" />
