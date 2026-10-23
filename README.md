# DaisyOS

DaisyOS is an Arch Linux-based desktop operating-system experience with a
custom C#/.NET and Avalonia shell. It combines a desktop, dock, launcher,
System Bar, Settings, Files, notifications, onboarding, system controls, and a
safe live-media recovery path.

## Project status

**Current milestone: bootable live-ISO alpha — complete.**

The source-bound live-alpha acceptance gate passed on 2026-07-16 with a fresh
1.5 GiB image. The same immutable ISO passed repository and embedded-payload
verification, BIOS and UEFI graphical boot, runtime-service checks, 60-second
stability holds, supervised shell recovery, and the real Restart and Shut down
menu actions. The accepted image had SHA-256
`dfe2ac9ee2083cc9520021766cd0cb007182553430cf533624371a1cee158621`.
That file has since been replaced locally by a newer compositor candidate, so
the path alone must not be treated as acceptance evidence.

This completes the non-installing live-ISO alpha milestone. DaisyOS is not yet
an installable or production-security-ready operating system.

The current development profile advances the next candidate to a KWin-managed
session with working wallpaper rendering and compositor-owned application
windows. The first 1.8 GiB rebuild (SHA-256
`fab605bc60919f1e040dbaf7570f05501cdd4c6930f3eedf44b8eb07ee091e17`)
was rejected after a stronger framebuffer review exposed a mostly black frame
behind a narrow wallpaper fragment. The obsolete full-screen GPU vibrancy host
was removed, and the corrected payload produces a full 1280x800 KWin desktop
in the VM. A new production ISO must still pass the complete acceptance gate
before it supersedes the accepted alpha.

### Recent Shell Improvements

- **Daily workflow improvements**: Faster Launcher search activation, Files tab and search shortcuts, safer Notes autosave and close behavior, keyboard file picking, cancellable Wi-Fi connection, and responsive App Store results. See [keyboard shortcuts](docs/daisyos-keyboard-shortcuts.md) and [UX validation coverage](docs/validation/ux-polish-2026-09-30.md) for tested behavior and remaining native desktop checks.
- **Refined Dual-Theme Engine**: Added a daytime paper-inspired Light Theme (`#F4F5F7` base, soft charcoal `#1F2328` text, crisp white cards, 7% subtle hover overlays) alongside a normalized Dark Theme (`#1E1E1E` chrome, 8% white hover highlights).
- **Ultra-Fast 6K Wallpaper Extraction**: Optimized `WallpaperColorExtractor` with strided 64x64 grid sampling and seed caching, reducing cold extraction time on 6K/8K images from ~4s to ~15ms (<0.01ms cached).
- **High-Contrast Shell Controls**: Redesigned `SearchBar` and `CaptionClose` buttons for 1:1 color matching with Settings buttons and title bar controls. Synchronized audio spectrum visualizer bars with media control accents.
- **Automated Test Gate**: Passed all 82 unit, service, ViewModel, and color contrast tests (`dotnet test`).

See [ROADMAP.md](ROADMAP.md) for detailed progress and
[docs/iso-build.md](docs/iso-build.md) for the live-image workflow.

## Build and test the shell

Requirements include the .NET 10 SDK on Linux.

```bash
dotnet restore
dotnet build
dotnet test
dotnet run --project src/DaisyOS.Shell
```

Run the complete repository gate with:

```bash
scripts/test.sh
```

## Build a live ISO

On an Arch Linux host with `archiso`, QEMU, ImageMagick, and `socat`:

```bash
scripts/publish-shell.sh
scripts/copy-shell-to-iso.sh
sudo scripts/build-iso.sh
scripts/validate-live-iso-alpha.sh out/DaisyOS-YYYY.MM.DD-x86_64.iso
```

ISO assembly needs temporary administrator access for the isolated Arch image
filesystem. VM tests attach no writable disk. Never test future installer or
partitioning work on a physical disk before it has passed explicit VM review.

## Alpha scope

The bootable live alpha is intended to prove that the disposable image boots
and provides a stable DaisyOS desktop preview. It includes bounded shell crash
recovery and real live-session service wiring.

It intentionally does not claim:

- Installation to disk—the installer remains mock-only.
- Signed public package distribution or safe update installation.
- Rollback, disk encryption, Secure Boot, or production authentication.
- Compatibility with every GPU, Wi-Fi adapter, display, or physical computer.
- Production readiness as a primary daily-driver operating system.

## Contributing

We welcome contributions! Please read our [**Contributing Guide**](CONTRIBUTING.md) for details on our code style ([`.editorconfig`](.editorconfig)), safe local build instructions, common development commands, and pull request procedures.

## Repository layout

- `src/DaisyOS.Shell` — desktop shell and system-app surfaces
- `src/DaisyOS.System` — guarded Linux service integrations
- `src/DaisyOS.Core` — shared models and infrastructure
- `src/DaisyOS.Installer` — safe mock-only installer UI
- `os/archiso/DaisyOS` — live ISO profile
- `scripts` — build, packaging, recovery, and acceptance automation
- `tests` — unit, ViewModel, service, and headless UI tests
- `docs` — architecture, testing, security, compositor, and release guidance

## Safety

DaisyOS development defaults to mock-safe destructive services. Real power and
system integration should be exercised only in the disposable live VM or an
explicitly prepared test environment. The project does not run disk formatting
or automatic package upgrades.
