# DaisyOS gaming repository audit — 2026-11-11

This is the Phase 0 inventory required by the [gaming engineering plan](DaisyOS_Gaming_Performance_Engineering_Plan.md). It describes repository configuration; the current development host is not a clean DaisyOS installation, so it cannot establish release-hardware performance or package behavior.

## Current implementation

| Area | Repository evidence | Finding |
|---|---|---|
| Architecture and kernel | `os/archiso/DaisyOS/profiledef.sh`, `packages.x86_64`, `airootfs/etc/mkinitcpio*` | The live image is Arch `x86_64`, ships the stock `linux` kernel and `linux-firmware`, and builds the Arch ISO initramfs. No alternate or gaming kernel is defined. The repository does not state a more specific minimum CPU feature level than the Arch x86_64 package baseline. |
| Boot and updates | `profiledef.sh`, `efiboot/`, `syslinux/`, `scripts/build-iso.sh`, `packaging/arch/` | Live ISO uses Syslinux for BIOS and systemd-boot for UEFI. The checked-in UEFI entry loads `/arch/boot/vmlinuz-linux` and the Arch ISO initramfs. ISO updates mean rebuilding the image; the Arch package definition is separate. No installed-system kernel update hook, boot entry management, or end-user OS updater is defined here. |
| Repositories | `pacman.conf`, `os/packages/desktop-gaming*.txt` | ISO configuration enables `core`, `extra`, and `multilib`. Gaming package profiles are Arch package lists. The NVIDIA profile adds `nvidia-open`, `nvidia-utils`, and `lib32-nvidia-utils`; there is no DaisyOS package repository configured for the ISO. |
| GPU stack | `os/packages/desktop-gaming*.txt`, `os/archiso/DaisyOS/packages.x86_64` | Mesa and `lib32-mesa`, Vulkan tools, and the 32-bit Vulkan loader are included. Mesa supplies the intended Intel/AMD userspace path; the separate NVIDIA profile supplies NVIDIA userspace and 32-bit libraries. No explicit hardware support matrix or GPU generation detection policy is committed. |
| Steam and compatibility | gaming package lists and `scripts/stage-gaming-integration.sh` | Steam, Lutris, Wine, GameMode, Gamescope, MangoHud, and 32-bit GameMode/MangoHud are packaged in the gaming profile. Proton is provided through Steam rather than a separately pinned OS package. Multilib is enabled. |
| Desktop/session | `docs/compositor.md`, `scripts/daisyos-start-session`, `scripts/daisyos-xorg-session` | Production target is KWin Wayland with XWayland. An Xorg path remains as a compatibility/recovery route. ISO startup scripts own the live session. |
| Power and game lifecycle | `power-profiles-daemon`, `powerdevil`, `gamemode`; `src/DaisyOS.System/Gaming/GameModeController.cs`; `src/DaisyOS.Shell/Services/GamingModeAutomation.cs` | Power Profiles Daemon and KDE PowerDevil are present; GameMode owns its supported temporary game adjustments. DaisyOS detects Steam/Lutris process trees and requests GameMode from the shell. No DaisyOS scheduler/thermal daemon or custom game-priority policy is present in the inspected path. |
| Rust/C# boundaries | `README.md`, `services/`, `src/DaisyOS.Shell/` | Repository guidance prefers small Rust resident brokers with zbus/systemd integration for new background services. The shell and gaming UI/automation are C#/.NET/Avalonia; existing GameMode integration is through a C# controller. A new privileged Rust daemon is not justified for Phase 1 diagnostics. |
| Settings UI | `src/DaisyOS.Shell/Apps/System/Settings/SettingsView.axaml.cs` | Gaming settings currently expose automatic Gaming Mode. Hardware status is represented by `LinuxGamingService`; the GUI is not a substitute for a complete shareable diagnostics report. |
| Benchmarks and validation | `scripts/analyze-game-frametimes.py`, `scripts/compare-game-frametimes.py`, `docs/validation/gaming-performance-2026-10-15.md` | Frame-time CSV analysis and repeated-run comparison exist. Past notes document idle-work reductions and a faster NVIDIA metrics query, but explicitly do not establish controlled game frame-time gains. ISO/package profile checks exist. The new one-command host report records GPU/driver, Vulkan availability evidence, packages, renderer, session and power state. |

## Hardware support snapshot

The only image architecture declared in the ISO profile is `x86_64`; no minimum CPU feature flags are specified by DaisyOS. The package strategy indicates intended support for Mesa-driven AMD/Intel GPUs and NVIDIA GPUs handled by the optional proprietary NVIDIA profile. This is a packaging intent, not a validated hardware matrix. Hybrid graphics, older NVIDIA generations, firmware edge cases, and 32-bit ICD execution still need physical-device validation.

## Gaps and risks

1. The shell's `LinuxGamingService` reports 32-bit loader presence based on a library file. The UI now says device enumeration is unverified; a working 32-bit ICD still has to be proven on target hardware.
2. The diagnostic gathers kernel, active GPU kernel driver, Vulkan output when `vulkaninfo` is available, package versions, renderer, session/compositor, and power profile. It does not yet capture VRR/refresh, thermal throttling, or actual 32-bit device enumeration.
3. The NVIDIA profile describes RTX 20-series/Turing and newer in its comment, but there is no runtime validation that the active GPU and installed module match that policy.
4. Benchmark notes are historical and hardware-specific. No scheduler, kernel, power, or IRQ change should be made until repeatable same-machine runs exist.
5. The ISO profile's bootability does not establish an installed-system update/rollback contract; gaming kernel experiments must wait for that recovery contract.

## First implementation slice

Phase 0's repository inventory is complete; physical-hardware baselines remain pending. Phase 1 now has a read-only `daisyos-gaming-diagnostics` host report, an optional i686 probe source/build path, and explicit ISO/package-profile dependencies for the 64-bit and 32-bit Mesa/Vulkan stack. Profile validation keeps NVIDIA libraries in the NVIDIA profile. Diagnostics distinguish loader presence from actual device enumeration. The current development host lacks the 32-bit compiler runtime, so probe execution and reference-GPU evidence remain follow-up work; no 32-bit readiness is claimed.

## Ownership map and next gates

- Package/profile and image correctness: `os/packages/`, `os/archiso/DaisyOS/`, `scripts/check-iso-profile.sh`.
- Runtime hardware and graphics observations: new `scripts/daisyos-gaming-diagnostics` command; UI status remains in `LinuxGamingService`.
- Game session lifecycle: `GamingModeAutomation` and `GameModeController`; no kernel/power tuning ownership is added in this milestone.
- Performance evidence: existing frame-time analyzer/comparator plus recorded hardware, driver, display, and power context.

Hardware release gates remain: (1) capture reports on supported physical GPUs, (2) build and verify actual 32-bit Vulkan device enumeration, and (3) validate Steam/native and Proton/DXVK/VKD3D launch paths. Phase 2 work can proceed as read-only discovery while those device checks are pending; do not claim compatibility or change kernel/runtime defaults without that evidence. Keep the stock kernel and current recovery paths intact.
