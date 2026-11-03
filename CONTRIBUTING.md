# Contributing to DaisyOS

Thank you for contributing to DaisyOS! This guide outlines our engineering standards, architecture, development workflows, and pull request process. These standards ensure smooth collaboration in a multi-person production environment, following best practices seen at Apple and Microsoft.

---

## 1. Repository Layout

```
├── .github/              # CI/CD workflows, pull request & issue templates
├── .editorconfig         # Unified formatting and code style across all editors
├── .gitattributes        # Line ending (LF) and binary file declarations
├── .gitignore            # Clean ignoring of build outputs, caches, and IDE files
├── Directory.Build.props # Global .NET build configuration and versioning
├── DaisyOS.sln           # Primary solution file
├── docs/                 # Architectural specifications, audits, and guides
│   ├── apps/             # App-specific documentation
│   ├── architecture/     # System architecture and design choreography
│   ├── benchmarks/       # Performance and startup benchmarks
│   ├── development/      # Contributor guides and command references
│   └── validation/       # QA audit logs and regression reports
├── external/             # Pinned third-party dependencies (Avalonia source build)
├── os/                   # OS packaging, ArchISO profiles, and systemd services
├── packaging/            # Distribution packaging (Arch PKGBUILD, KWin scripts, portals)
├── scripts/              # Build, test, acceptance, and developer automation scripts
├── services/             # Core OS daemon services (DaisyLaunch)
├── src/                  # Primary application and system source code
│   ├── DaisyOS.Core/     # Shared contracts, models, and file storage primitives
│   ├── DaisyOS.System/   # Linux system services (Audio, Bluetooth, Displays, Gaming)
│   ├── DaisyOS.Shell/    # Main Avalonia desktop shell and application UI
│   ├── DaisyOS.Files/    # Native file manager application
│   ├── DaisyOS.Notes/    # Notes application
│   ├── DaisyOS.Settings/ # System settings application
│   ├── DaisyOS.Installer/# System installer
│   └── native/           # C/C++ native helpers (window decoration, portal capture)
└── tests/                # Automated unit and integration test suites
```

---

## 2. Prerequisites & Environment Setup

- **Operating System:** Linux (Arch Linux recommended, Ubuntu/Debian supported for development)
- **.NET SDK:** 10.0+ (Check with `dotnet --version`)
- **Native Build Tools:** `gcc`/`clang`, `cmake` (3.25+), `make` or `ninja`
- **Compositor & Display:** KWin 6.x (Wayland), `wayland-scanner`, `pkg-config`
- **Libraries:** `polkit-agent-1`, `wayland-client`, `libpulse`, `bluez`

---

## 3. Developer Workflows & Common Commands

### A. Testing Individual Components (Safe & Fast)
To prevent excessive resource consumption on local workstations, run focused tests rather than unbounded solution-wide builds:

```bash
# Run core system and shell unit tests
dotnet test tests/DaisyOS.Tests/DaisyOS.Tests.csproj

# Run shell UI tests
dotnet test tests/DaisyOS.Shell.Tests/DaisyOS.Shell.Tests.csproj

# Run validation scripts
scripts/check-branding.sh
scripts/check-iso-profile.sh
scripts/test-release-scripts.sh
scripts/check-packaging.sh
```

### B. Nested KWin Prototype
Test shell changes safely inside a nested Wayland window without affecting your active desktop session:

```bash
# Fast build without ReadyToRun precompilation
scripts/publish-shell.sh --fast

# Launch inside nested KWin window
scripts/run-kwin-prototype.sh
```

### C. Running the Standalone Shell
Run the shell directly against real desktop services:

```bash
DAISYOS_SHELL_SERVICES=real dotnet run --project src/DaisyOS.Shell
```

### D. Native Development Session (Hot Reloading Desktop)
In a dedicated native development session:

```bash
# Rebuilds DaisyOS and replaces only the layer-shell process (KWin and apps stay open)
daisyos-native-development restart

# Inspect current RAM reserve and protected memory boundaries
daisyos-native-development memory status
```

### E. KWin Window Decoration
```bash
# One-time installation of the native titlebar plugin
sudo scripts/install-kwin-window-decoration.sh

# Reload the light/dark decoration palette without restarting applications
daisyos-native-development decoration reload

# Inspect or toggle the native titlebar
daisyos-native-development decoration status
daisyos-native-development decoration disable
```

### F. Production / ISO Publish
```bash
# Full production publish with ReadyToRun precompilation
scripts/publish-shell.sh
```

---

## 4. Engineering Standards & Quality Gates

### A. Code Style & Formatting
- All code must comply with the root [`.editorconfig`](.editorconfig).
- Run `dotnet format --verify-no-changes` before opening a pull request.
- Keep line endings normalized to LF (enforced via [`.gitattributes`](.gitattributes)).

### B. Repository Hygiene
- **No binary build artifacts in Git:** Never commit object files (`.o`), shared libraries (`.so`, `.dll`), test logs (`.trx`), or compiled caches (`__pycache__`, `CMakeCache.txt`).
- **No loose files in repository root:** Keep root tidy. General documentation belongs in `docs/`, scripts in `scripts/`.
- **No spaces in file names:** Use `kebab-case` or `PascalCase` for all file and folder names.

### C. Localization
- User-facing strings in `DaisyOS.Shell` must be localized using `Loc.Get("...")` or `{DynamicResource Loc_...}`.
- Run `python3 scripts/localization/update-catalog.py` when adding new localized strings to update `Messages.resx`.

---

## 5. Submitting Changes

1. **Branching:** Create a feature branch off `main` with a descriptive name:
   `git checkout -b feature/your-feature-name` or `bugfix/issue-description`
2. **Atomic Commits:** Make focused, well-described commits.
3. **Verify:** Run appropriate test and validation scripts before pushing.
4. **Pull Request:** Open a pull request using our [Pull Request Template](.github/PULL_REQUEST_TEMPLATE.md).
