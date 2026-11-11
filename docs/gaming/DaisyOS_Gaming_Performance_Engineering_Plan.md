# DaisyOS Gaming Performance Engineering Plan

## Codex implementation specification

**Status:** Proposed engineering roadmap\
**Target:** Arch-based DaisyOS desktop operating system\
**Primary objective:** Deliver best-in-class Linux gaming performance,
compatibility, frame pacing, responsiveness and ease of use, targeting
the quality of CachyOS without sacrificing DaisyOS's desktop experience
or support for older hardware.

------------------------------------------------------------------------

# 0. Instructions to the Codex agent

Treat this document as an engineering specification and implementation
roadmap, not permission to make broad system changes immediately.

## Mandatory working rules

1.  **Inspect the repository before changing anything.** Identify the
    actual OS packaging, installer, image build, kernel, bootloader,
    desktop shell, system services, settings UI, update mechanism,
    supported architectures and current Rust/C# boundaries. Do not
    assume paths or architecture from this document.
2.  **Produce an implementation inventory first.** Map each proposed
    capability to existing DaisyOS components or identify the missing
    component. Record current package names and service conventions.
3.  **Implement incrementally.** Use small, reviewable changes. Do not
    attempt the entire gaming stack in one patch.
4.  **Preserve a bootable, recoverable system.** Never replace the only
    working kernel, remove the fallback kernel, overwrite bootloader
    entries without a recovery entry, or make experimental tuning
    mandatory.
5.  **Do not apply every tweak found online.** A tweak is accepted only
    after reproducible measurements show a benefit on relevant hardware
    without unacceptable regressions.
6.  **Never promise a universal FPS increase.** Different games, CPUs,
    GPUs, drivers, displays and bottlenecks respond differently.
7.  **Keep the normal desktop first-class.** Gaming optimizations must
    not make DaisyOS's shell, window manager/compositor, audio,
    networking, accessibility or background services unreliable.
8.  **Use supported upstream interfaces where possible.** Prefer kernel
    sysfs, systemd, D-Bus, power-profiles-daemon, GameMode, Mesa, NVIDIA
    interfaces and documented application APIs over brittle scripts.
9.  **Do not write arbitrary values to sysfs.** Discover available
    governors, EPP values, driver capabilities, GPU interfaces and
    kernel parameters at runtime. Validate every value before applying
    it.
10. **Treat all game processes as untrusted user-space applications.**
    The gaming service must not execute arbitrary Steam launch options
    or game-provided commands as root.
11. **Keep privileged operations minimal.** Use a small, auditable
    system service or narrowly scoped helpers. The desktop UI must not
    run as root.
12. **Document every non-default setting**, including purpose, hardware
    applicability, expected effect, risk, detection method, rollback and
    benchmark evidence.
13. **Use the current upstream documentation and source code.** Package
    names, kernel options, drivers and interfaces change. Verify them
    for the target Arch repositories and supported hardware at
    implementation time.
14. **Do not silently change the user's settings.** Explain changes,
    preserve user overrides and provide an easy reset.
15. **Do not break secure boot, disk encryption, suspend/resume, hybrid
    graphics, multi-monitor setups or accessibility to chase a
    theoretical performance gain.**

## Required first deliverable

Before coding, create a short repository audit containing:

-   Current kernel and kernel packaging approach.
-   Bootloader and kernel-install/update mechanism.
-   Supported CPU architectures and minimum CPU baseline.
-   Current Mesa, Vulkan, NVIDIA and 32-bit graphics package strategy.
-   Steam, Proton, Wine and multilib state.
-   Current compositor/window manager and session types (Wayland/X11 if
    supported).
-   Existing power management, GameMode, scheduler, process-priority and
    thermal-management services.
-   Existing Rust service conventions and D-Bus/systemd integration.
-   Existing settings UI architecture.
-   Current ISO/install process and package repositories.
-   Existing benchmark or test infrastructure.
-   Gaps, risks and proposed phased implementation.

Do not start modifying kernel, bootloader or power settings until this
audit is complete.

## Implementation status (updated 2026-11-11)

| Plan milestone | Status | Evidence / remaining gate |
|---|---|---|
| Phase 0 — repository audit and baseline | **Repository audit complete; hardware baseline pending** | [Repository audit](repository-audit-2026-11-11.md) inventories image, packages, kernel/boot, session, services and validation. No controlled physical-GPU baseline has been captured. |
| Phase 1 — gaming foundation | **Repository implementation complete; hardware acceptance pending** | The report, optional i686 probe, explicit Vulkan package dependencies, and profile guards are implemented. This host lacks Steam and the 32-bit Vulkan stack; physical Vulkan enumeration and Steam game launch have not been verified, so Phase 1 release exit criteria are not yet met. |
| Phase 2 — compatibility stack | **Repository integration implemented; runtime acceptance pending** | Gaming profiles include UMU; Settings links to Steam's per-game compatibility controls and explains reset behavior; docs cover per-user Proton-GE discovery and isolated UMU prefixes. Diagnostics inventories Proton tools, standard DXVK/VKD3D payload markers, UMU, and NTSYNC without enabling it. Native, DXVK, VKD3D, controller, overlay, and anti-cheat acceptance on supported hardware remain open. |
| Phase 3 — gaming tools | **Repository integration implemented; runtime acceptance pending** | Gaming Mode owns GameMode; Settings exposes Gaming Mode, automatic detection, MangoHud monitor visibility, GOverlay, Steam per-game options, and Gamescope guidance. Gamescope/GOverlay are in gaming profiles and the live ISO. No global Gamescope wrapper or frame cap is applied. Tool launch/restore and conflict checks still need real-game acceptance. |
| Phase 4 — CPU and power profiles | **Repository implementation complete; device acceptance pending** | Diagnostics inventory per-policy driver, available/current governors, EPP, frequency limits, AMD/Intel pstate status, heterogeneous core types, and PPD profiles. Gaming Mode uses PPD's advertised `performance` profile by default, snapshots the prior profile, and restores it only if it remains active; users can disable this or separately opt into battery boost. GameMode retains CPU/process tuning ownership; DaisyOS writes no sysfs values. Physical AC/battery transitions, profile restoration, unsupported hardware and thermal acceptance remain pending. |
| Phase 5 — `daisy-gamingd` | **Repository implementation complete; runtime acceptance pending** | A per-user Rust/zbus coordinator owns Steam/Lutris process-tree detection, GameMode requests, AC-aware PPD profile transactions, structured logs, same-UID D-Bus authorization and restart recovery. The shell is a D-Bus client and retains UI/overlay telemetry. Packaged user service is included in shell release, ISO staging and Arch packaging. Booted-session D-Bus, game lifecycle, profile and crash-recovery acceptance remain pending. |
| Phase 6 — Gaming kernel | **Repository candidate and capture protocol implemented; physical acceptance pending** | [Kernel evaluation](kernel-evaluation-2026-11-11.md) documents the stock fallback, optional `linux-zen` plus matching headers, NVIDIA DKMS path, and CachyOS review without changing the live ISO or defaults. Capture tooling records kernel ownership/config, NVIDIA/DKMS state, session resources and shell responsiveness. Candidate boot, recovery, three-run A/B evidence and NVIDIA compatibility remain open. |
| Phases 7–10 | **Not started** | No advanced scheduler policy, compositor gaming policy, package optimization or continuous benchmark suite has been implemented under this plan. |

This status records repository work, not general release readiness. Do not
mark hardware-dependent checklist items complete until the corresponding
physical-device evidence is recorded.

------------------------------------------------------------------------

# 1. Product goals and performance philosophy

DaisyOS Gaming should optimize for the whole gaming experience, not a
single FPS number.

## 1.1 Primary goals

1.  High average FPS where the hardware permits it.
2.  Strong 1% and 0.1% low FPS.
3.  Consistent frame pacing and low frame-time variance.
4.  Low input-to-display latency.
5.  Good audio, controller and network responsiveness.
6.  Fast game launch and low loading stutter.
7.  Reliable Windows-game compatibility through Proton.
8.  Correct GPU driver and Vulkan configuration out of the box.
9.  Good laptop performance without excessive heat, fan noise or battery
    drain.
10. Low idle RAM, CPU and background I/O overhead.
11. A responsive desktop while games run.
12. Safe behavior on older hardware and systems with only 8 GB RAM.

## 1.2 Non-goals

-   Do not maximize every CPU clock at all times.
-   Do not pin every game to all physical cores.
-   Do not disable SMT/Hyper-Threading globally.
-   Do not force a real-time kernel.
-   Do not disable security mitigations globally.
-   Do not disable swap or zram blindly.
-   Do not disable transparent huge pages globally without per-workload
    evidence.
-   Do not force a single scheduler on every CPU generation.
-   Do not force NVIDIA-specific settings on AMD/Intel or vice versa.
-   Do not force a particular Proton version for all games.
-   Do not enable every overlay or monitoring poll continuously.
-   Do not make experimental kernel patches part of the only supported
    kernel.
-   Do not sacrifice desktop stability or laptop thermals for a
    benchmark screenshot.

## 1.3 Performance hierarchy

Prioritize work in this order:

1.  Correct drivers and hardware acceleration.
2.  Correct 64-bit and 32-bit graphics/Vulkan stack.
3.  Game compatibility and Proton runtime.
4.  Correct CPU/GPU power and thermal behavior.
5.  Frame pacing, compositor and display configuration.
6.  Memory and storage behavior.
7.  Process scheduling and game workload isolation.
8.  Kernel build and compile-time optimization.
9.  Experimental scheduler, IRQ and kernel tuning.

A broken Vulkan stack cannot be fixed with a faster scheduler. A
GPU-bound game will not gain meaningful FPS from CPU affinity tweaks.

------------------------------------------------------------------------

# 2. Architecture: DaisyOS Gaming Platform

Build a coherent platform rather than a pile of unrelated scripts.

## 2.1 Proposed components

Use existing DaisyOS conventions where possible. Names below are
proposed logical names, not mandatory repository paths.

### A. `daisy-gamingd` --- system gaming coordinator

A small Rust daemon responsible for:

-   Detecting supported hardware and capabilities.
-   Tracking active games and gaming sessions.
-   Applying and removing temporary performance profiles.
-   Coordinating with existing power-management services.
-   Managing supported scheduler integration.
-   Monitoring thermal and power constraints.
-   Applying reversible, allow-listed optimizations.
-   Exposing status and controls through a narrow D-Bus API.
-   Recording diagnostics and optimization failures.
-   Restoring previous state after game exit, crash, daemon restart or
    shutdown.

The daemon should not become a game launcher, compatibility layer or GPU
driver manager. Delegate those jobs to the established tools and package
manager.

### B. `daisy-gaming-cli`

A small diagnostic and control CLI, if useful to the existing project:

-   `status`
-   `hardware`
-   `profile list`
-   `profile show <name>`
-   `profile set <name>`
-   `session list`
-   `session explain`
-   `diagnostics`
-   `reset`

Use actual supported commands and permissions. The CLI must not bypass
daemon policy.

### C. DaisyOS Settings integration

Expose:

-   Current gaming profile.
-   Detected CPU/GPU and active driver.
-   Whether hardware acceleration and Vulkan are working.
-   Active game session.
-   Power/thermal status.
-   Optional overlay settings.
-   Optional Gamescope settings.
-   Scheduler status and whether it is experimental.
-   Clear explanations and a reset button.

Do not expose dangerous raw kernel knobs to ordinary users.

### D. Packaging and image integration

Create well-defined package groups/metapackages:

-   Core gaming support.
-   AMD gaming support.
-   Intel gaming support.
-   NVIDIA gaming support.
-   Optional compatibility tools.
-   Optional performance monitoring.
-   Optional experimental scheduler tools.
-   Optional creator/streaming tools.

Use the actual DaisyOS package and ISO conventions discovered in the
audit.

## 2.2 Service boundaries

Suggested ownership:

  -----------------------------------------------------------------------
  Responsibility                      Preferred owner
  ----------------------------------- -----------------------------------
  CPU power profile                   power-profiles-daemon or supported
                                      platform service

  Game-specific optimization request  GameMode / `daisy-gamingd` policy

  CPU scheduler                       kernel default or one selected
                                      sched_ext loader

  GPU driver                          distribution package manager

  Vulkan implementation               Mesa / NVIDIA packages

  Windows game compatibility          Steam Proton / Wine / UMU

  Game microcompositor                Gamescope

  Performance overlay                 MangoHud

  Frame cap / VRR                     game, Gamescope or compositor---one
                                      deliberate owner

  Thermal protection                  firmware, kernel thermal framework,
                                      vendor driver

  Desktop animations and effects      DaisyOS shell/compositor
  -----------------------------------------------------------------------

Avoid two services continuously fighting over the same setting. In
particular, do not run multiple competing CPU-priority managers or
independent scheduler controllers.

------------------------------------------------------------------------

# 3. Hardware detection and capability model

Create a hardware capability model before applying optimizations.

## 3.1 CPU discovery

Collect, without assuming a specific vendor:

-   CPU vendor, family, model and stepping.
-   Microarchitecture where reliably detectable.
-   Logical CPU count.
-   Physical core topology.
-   SMT status.
-   Hybrid core topology (performance/efficiency cores).
-   Cache topology and NUMA nodes.
-   Supported instruction sets.
-   Current CPU frequency driver.
-   Available governors.
-   Available EPP preferences.
-   Available min/max frequency limits.
-   Current power profile.
-   Thermal and throttling telemetry where available.
-   Virtualization/container status where relevant.

Use kernel interfaces such as `/proc/cpuinfo`,
`/sys/devices/system/cpu/`, `lscpu` and CPUID libraries. Treat the
kernel's exposed topology as authoritative for runtime scheduling
decisions.

### Instruction-set safety

-   Generic DaisyOS packages must run on the declared minimum CPU
    baseline.
-   Do not compile the base OS with `-march=native`.
-   Do not ship x86-64-v3 binaries to CPUs that lack the required
    features.
-   Detect CPU feature support at runtime before offering optimized
    package variants.
-   Ensure boot-critical components remain compatible with the minimum
    supported CPU.
-   Provide generic fallback packages.
-   Add automated checks that scan binaries for unsupported instruction
    requirements where practical.

The user's older Intel Core i7-3770K is a useful compatibility test: it
does not support AVX2 and must not be treated as x86-64-v3 capable.

## 3.2 GPU discovery

Detect all GPUs, including hybrid laptop configurations:

-   PCI vendor/device IDs.
-   AMD, Intel or NVIDIA vendor.
-   Kernel driver currently bound to each device.
-   Driver version.
-   Mesa version, if applicable.
-   Vulkan physical devices and ICDs.
-   OpenGL renderer.
-   32-bit Vulkan availability.
-   GPU memory and VRAM where available.
-   GPU power/performance controls exposed by the driver.
-   Display outputs and active modes.
-   Hybrid/offload capabilities.
-   Hardware video encode/decode capabilities.

Use `lspci`, sysfs, `vulkaninfo` (when installed), `glxinfo` (when
installed), DRM interfaces and vendor-supported interfaces. Do not parse
fragile human-formatted command output if a stable machine-readable
interface exists.

### GPU validation

Provide a diagnostic report that answers:

-   Is the intended GPU detected?
-   Is the expected kernel driver loaded?
-   Is the correct Vulkan ICD installed?
-   Does 64-bit Vulkan work?
-   Does 32-bit Vulkan work?
-   Is the game likely to use the intended GPU?
-   Is PRIME/offload available on hybrid systems?
-   Is the system using software rendering accidentally?

Clearly flag `llvmpipe` or another software renderer when a hardware GPU
should be active.

## 3.3 Capability-based profiles

Represent capabilities, not assumptions:

``` text
CpuCapabilities
  vendor
  topology
  hybrid_topology
  supported_features
  cpufreq_driver
  available_governors
  available_epp
  thermal_telemetry

GpuCapabilities[]
  vendor
  device_id
  kernel_driver
  userspace_driver
  vulkan_64
  vulkan_32
  power_controls
  hybrid_offload

PlatformCapabilities
  ac_power
  battery
  firmware_profile
  display_vrr
  display_refresh_rates
  compositor
  session_type
  kernel_features
```

Profiles must be generated from detected capabilities. A setting that is
unsupported should be hidden or shown as unavailable, not force-applied.

------------------------------------------------------------------------

# 4. Kernel strategy

## 4.1 Maintain multiple bootable kernel choices

Recommended package concepts:

1.  **DaisyOS Default Kernel** --- stable, broadly compatible and
    tested.
2.  **DaisyOS Gaming Kernel** --- performance-oriented, tested gaming
    configuration.
3.  **DaisyOS LTS Kernel** --- fallback/stability option.

Keep the standard Arch-compatible kernel available as a recovery option
where practical.

Do not make the gaming kernel the only kernel. Ensure kernel and
matching module packages update atomically, especially for NVIDIA DKMS
or prebuilt modules.

## 4.2 Study and selectively adopt CachyOS kernel work

Study the current CachyOS kernel source, PKGBUILDs, configuration and
patchset. Evaluate each patch independently.

Investigate:

-   Kernel config changes.
-   BORE scheduler availability and implementation.
-   EEVDF improvements.
-   POC CPU selection work, if applicable to the target kernel version.
-   PREEMPT_DYNAMIC modes.
-   Timer frequency choices.
-   AMD P-State improvements.
-   Filesystem and memory-management changes.
-   Compiler and LTO options.
-   AutoFDO/Propeller profile workflows.
-   Architecture-specific build variants.
-   NVIDIA out-of-tree module compatibility.

Do not blindly copy a moving patchset. Record the upstream commit, patch
provenance, license, kernel version, config difference, test evidence
and rollback path.

## 4.3 Scheduler choices

### Default scheduler

Keep the upstream kernel scheduler as a reliable baseline. Modern EEVDF
is the baseline to beat, not an obsolete scheduler to replace
automatically.

### BORE

Investigate BORE as an optional gaming/interactive scheduler patch where
compatible with the selected kernel.

Test: - Average FPS. - 1% and 0.1% lows. - Frame-time variance. -
Desktop responsiveness under CPU contention. - Compilation throughput. -
Audio glitches. - Power and thermal behavior.

### sched_ext

Investigate sched_ext as an optional experimental feature, not a
mandatory dependency.

Potential schedulers include options maintained in the current
`scx-scheds` ecosystem. Names, maturity and behavior change; inspect
current upstream documentation before selecting one.

Rules: - Never assume a sched_ext scheduler is universally faster. -
Never run two scheduler loaders simultaneously. - Keep a watchdog and
automatic fallback to the kernel's default scheduler. - Ensure the
scheduler can be disabled from recovery mode. - Detect kernel
BPF/sched_ext support before offering the feature. - Handle daemon
crash, scheduler crash, kernel upgrade and suspend/resume. - Avoid
requiring a scheduler-specific patched `power-profiles-daemon` unless
DaisyOS deliberately maintains and tests that patch.

### Scheduler selection policy

Start with: - Default: kernel scheduler. - Gaming experiment: one tested
scheduler/profile on supported systems. - Power save: use normal
power-management policy unless an alternative scheduler proves
beneficial. - Low latency: only expose if a workload-specific test
supports it.

Do not automatically enable `scx` merely because it is installed.

## 4.4 Kernel preemption and tick rate

Benchmark supported PREEMPT_DYNAMIC modes and timer frequencies on
representative systems.

Do not assume: - 1000 Hz is always better than 250 Hz. - Full preemption
always improves gaming. - Real-time kernels improve FPS. - Lower tick
rates always save power without affecting latency.

Measure throughput, frame-time tails, idle power, wakeup latency and CPU
overhead.

Use a conservative tested default. Keep experimental kernel variants
separate.

## 4.5 Kernel compilation optimization

Evaluate: - Clang versus GCC. - ThinLTO. - Link-time optimization. -
PGO. - AutoFDO. - Propeller where supported. - CPU-specific compiler
targets. - Kernel configuration trimming.

Use reproducible build infrastructure. Build and sign packages
consistently.

AutoFDO/PGO profiles must represent real DaisyOS workloads, not only a
single synthetic benchmark. Include: - Desktop idle and interactive
workloads. - Steam startup and game launch. - Representative game
workloads. - Shader compilation. - File and network I/O. - Audio and
compositor activity.

Keep a generic kernel build for broad compatibility. Do not let
CPU-specific compiler flags make the kernel unbootable on older
supported CPUs.

## 4.6 Kernel parameters

Review parameters such as: - `mitigations=` - `preempt=` -
`nowatchdog` - `nmi_watchdog=` - `nohz_full` - `isolcpus` -
`rcu_nocbs` - `transparent_hugepage=` - `vm.max_map_count`

Do not add aggressive parameters globally by default.

In particular: - Do not disable security mitigations for FPS. - Do not
disable watchdogs globally. - Do not use `nohz_full` as a generic
desktop gaming tweak. - Do not isolate cores without a measured,
topology-aware design. - Do not disable THP globally; allocator and game
behavior differ. - Set `vm.max_map_count` to a compatibility-safe value
supported by current Arch/Proton guidance (at least the commonly
documented 1,048,576 baseline unless upstream guidance changes). - Keep
a clear list of kernel arguments and explain each one.

------------------------------------------------------------------------

# 5. CPU performance management

## 5.1 Use the correct driver

Support the kernel's available CPU frequency drivers:

-   `amd-pstate` / `amd-pstate-epp` where supported.
-   `intel_pstate` where supported.
-   `acpi-cpufreq` or other platform drivers where applicable.

Do not force one driver across all machines.

## 5.2 EPP and governor policy

Inspect each CPU policy's available values. Use the platform's supported
controls.

Possible profile intent:

  -----------------------------------------------------------------------
  Profile                             Policy intent
  ----------------------------------- -----------------------------------
  Efficiency                          Favor energy efficiency while
                                      remaining responsive

  Balanced                            Normal platform behavior

  Performance                         Favor fast response and sustained
                                      performance within thermal/power
                                      limits
  -----------------------------------------------------------------------

Use the actual supported EPP strings, governors and interfaces
discovered at runtime. Do not assume every system exposes `performance`,
`balance_performance` or the same EPP choices.

Prefer power-profiles-daemon or another single authoritative power
manager where it supports the hardware. Avoid competing with TLP, tuned,
auto-cpufreq, vendor daemons or firmware utilities.

## 5.3 Hybrid CPUs

On Intel hybrid CPUs and other heterogeneous systems:

-   Detect performance and efficiency cores from kernel topology.
-   Do not assume CPU numbering corresponds to core type.
-   Do not pin all game threads to P-cores without evidence.
-   Test game main/render threads and background workers separately.
-   Preserve system responsiveness and efficient background scheduling.
-   Avoid starving E-cores or background services.
-   Account for firmware and scheduler changes across CPU generations.

## 5.4 SMT / Hyper-Threading

Do not disable SMT globally.

Benchmark: - SMT enabled. - SMT disabled only as an optional test. - Any
scheduler-specific physical-core preference.

Some games benefit from SMT; others are sensitive to sibling contention.
Use per-platform evidence, not a universal rule.

## 5.5 CPU affinity and core isolation

Do not apply static CPU pinning to all games.

If experimenting: - Use topology-aware CPU sets. - Keep IRQ, audio,
compositor and system housekeeping in mind. - Never strand essential
system work. - Detect hybrid cores and NUMA. - Ensure the game can use
enough CPUs. - Provide immediate rollback. - Benchmark with background
load and real games.

Core isolation is an advanced opt-in profile only after demonstrated
gains.

## 5.6 Process priority

Use GameMode or a single coordinated policy for temporary game priority.

Do not blindly: - Set every game to real-time priority. - Set `nice -20`
for every process. - Set all game I/O to the highest priority. - Boost
every Steam child process equally. - Give a game priority over audio,
input, compositor or kernel-critical work.

Use a conservative, tested policy. Avoid starvation and deadlocks.

------------------------------------------------------------------------

# 6. GPU drivers and graphics stack

Correct GPU support is a top-tier priority.

## 6.1 AMD

Support and test: - Kernel `amdgpu`. - Mesa OpenGL. - RADV Vulkan. -
64-bit and 32-bit Vulkan. - Correct firmware packages. - Hardware video
acceleration where supported. - PRIME/hybrid configurations. -
DisplayPort/HDMI, VRR and multi-monitor operation.

Do not install AMDGPU-PRO components by default for ordinary gaming.
Prefer the standard open Mesa/RADV stack unless a specific supported
workload requires otherwise.

Investigate `RADV_PERFTEST` and other Mesa debug/experimental variables
only as per-game opt-in settings. Do not set experimental variables
system-wide.

## 6.2 Intel

Support and test: - Kernel `i915` or `xe` as appropriate for the
hardware and kernel. - Mesa ANV Vulkan. - Mesa OpenGL. - 32-bit
Vulkan. - Firmware. - Hybrid GPU offload. - Hardware video acceleration
where supported.

Do not assume the newest driver is always appropriate for every Intel
generation.

## 6.3 NVIDIA

Support both the proprietary driver and supported open kernel-module
packaging where appropriate.

Requirements: - Kernel module matches the running kernel. - Correct
user-space libraries. - 32-bit NVIDIA libraries for Steam/Proton. -
Vulkan ICD. - DRM KMS configured according to supported driver/session
requirements. - PRIME render offload on hybrid systems. - Suspend/resume
and multi-monitor testing. - Explicit synchronization support and
Wayland compatibility verified for the selected driver. - Gamescope
compatibility tested with the actual driver branch.

Do not hard-code one NVIDIA driver branch forever. Define a tested
packaging policy and upgrade/rollback strategy.

Do not apply AMD-specific environment variables to NVIDIA.

## 6.4 32-bit support

Steam and older games may need 32-bit libraries. Verify the complete
32-bit stack:

-   32-bit Vulkan loader.
-   32-bit vendor Vulkan driver.
-   32-bit OpenGL libraries.
-   32-bit GameMode library if used.
-   Required compatibility dependencies.

A 64-bit Vulkan test alone is insufficient.

## 6.5 Vulkan validation

Create a `daisy-gaming diagnostics` report with:

-   Vulkan loader version.
-   Enumerated Vulkan devices.
-   Active driver and ICD.
-   32-bit and 64-bit availability.
-   Renderer identity.
-   Missing or conflicting ICD warnings.
-   Software rendering warnings.

Test both native Vulkan and Proton/DXVK/VKD3D launch paths.

## 6.6 GPU power and clocks

Use only vendor-supported interfaces and driver policy.

AMD: - Investigate supported power profiles and
`power_dpm_force_performance_level` only where exposed and documented. -
Do not write arbitrary values to `pp_od_clk_voltage`. - Do not enable
overclocking by default.

NVIDIA: - Use supported NVIDIA settings and power management
interfaces. - Do not assume clock locking is available or safe. - Do not
require persistence mode for every desktop.

Intel: - Use documented driver controls and platform policy. - Avoid
unsupported register manipulation.

Default to driver/firmware-managed dynamic clocks. Gaming mode should
prevent inappropriate power-saving behavior where possible, not force
unsafe clocks or bypass thermal limits.

------------------------------------------------------------------------

# 7. Proton, Wine and Windows game compatibility

## 7.1 Steam

Provide: - Steam package from the appropriate trusted repository. -
Multilib enabled and validated. - Correct 32-bit graphics libraries. -
Correct Vulkan runtime. - Controller support. - Steam overlay support. -
Game launch and library integration. - Optional Steam Big Picture/TV
experience without making DaisyOS a console-only OS.

## 7.2 Proton versions

Offer: - Valve Proton stable. - Valve Proton Experimental as an optional
choice. - Proton-GE as an optional compatibility tool, subject to its
own licensing and distribution rules.

Do not force one Proton build for all games.

Provide clear controls for selecting Proton versions and resetting a
game's compatibility configuration.

## 7.3 DXVK and VKD3D-Proton

Validate: - Direct3D 9/10/11 through DXVK. - Direct3D 12 through
VKD3D-Proton. - Correct Vulkan device selection. - Correct 32-bit and
64-bit libraries. - Shader cache behavior. - Per-game environment
variable handling.

Do not replace the versions bundled or managed by Proton unless there is
a specific tested reason.

## 7.4 NTSYNC and synchronization

Investigate current kernel and Proton support for NTSYNC.

-   Detect kernel support.
-   Verify matching user-space/runtime support.
-   Enable only through the supported Proton/runtime path.
-   Fall back cleanly to the supported synchronization mechanism.
-   Benchmark representative CPU-bound games.
-   Avoid claiming a benefit when the game does not use the feature.

Keep Wine/Proton synchronization settings per runtime or per game where
appropriate.

## 7.5 UMU and non-Steam games

Investigate UMU as a supported way to run Proton-based environments
outside Steam.

Ensure: - Runtime consistency. - Per-game prefixes. - Correct
environment isolation. - No root execution. - User data remains in the
user's home directory. - Clean uninstall and prefix management.

## 7.6 Anti-cheat and compatibility transparency

Do not promise every Windows game works.

Provide a clear compatibility information experience: - Native Linux. -
Proton known-good. - Proton requires configuration. - Anti-cheat
unsupported or publisher-dependent. - Unknown.

Do not bypass anti-cheat or modify games in ways that violate publisher
rules. Make compatibility status transparent.

------------------------------------------------------------------------

# 8. GameMode and the DaisyOS gaming session lifecycle

## 8.1 GameMode

Package and configure GameMode where appropriate.

Check: - 64-bit and 32-bit libraries. - User service/socket
availability. - Permissions and group membership requirements. - Whether
the game actually requests GameMode. - Whether the desired settings are
supported by the current GameMode version.

GameMode can request temporary system optimizations. It should not fight
with another service that manages the same settings.

## 8.2 DaisyOS session lifecycle

The service should model a gaming session:

``` text
Idle
  -> GameDetected
  -> CapabilitiesValidated
  -> ProfilePrepared
  -> GameRunning
  -> GameExited
  -> StateRestored
```

Failure paths:

``` text
Game crashes       -> detect exit -> restore
Daemon crashes     -> systemd restarts -> reconcile state
Scheduler fails    -> stop scheduler -> kernel default scheduler
Suspend/resume     -> re-detect capabilities -> restore valid profile
Power source changes -> recompute policy
GPU disappears     -> stop GPU-specific actions -> preserve safe profile
System shutdown    -> restore or allow kernel/firmware defaults safely
```

## 8.3 Game detection

Use reliable process/session integration. Avoid matching process names
alone where possible.

-   Track Steam game IDs or launch-session metadata when available.
-   Track child processes and wrapper processes.
-   Handle games that spawn launchers and then replace themselves.
-   Handle multiple games and multiple users.
-   Avoid treating Steam's UI as a game.
-   Allow a user to manually start/stop a gaming profile.
-   Never execute a process-supplied command as root.

## 8.4 State snapshot and restoration

Before changing a setting: - Read current value. - Record the exact
original value. - Record which component owns it. - Apply only if
supported and permitted. - Verify the new value. - Restore only if the
current value still matches the value DaisyOS set, or if ownership can
be established.

This prevents DaisyOS from overwriting a change made by the user or
another legitimate service during the session.

Use a transaction-like session model and handle partial failure. A
failure halfway through profile activation must roll back all
successfully applied changes.

------------------------------------------------------------------------

# 9. Gamescope, compositor and display pipeline

## 9.1 Gamescope

Offer Gamescope as an optional, well-integrated game microcompositor.

Support: - Nested mode on the desktop. - Fullscreen modes where
supported and intentionally configured. - Resolution scaling. -
Upscaling options supported by the installed version and GPU. - Frame
limiting. - Refresh-rate selection where supported. - HDR only when the
complete display/driver/compositor path supports it. - Per-game
configuration.

Do not automatically wrap every game in Gamescope. Some games, drivers,
launchers or overlays may regress.

Test AMD, Intel and NVIDIA separately, and test both Wayland and X11
sessions if DaisyOS supports both.

## 9.2 DaisyOS compositor

Gaming integration must preserve a responsive desktop.

Investigate: - Correct Wayland presentation and explicit synchronization
support. - Direct scanout eligibility. - Fullscreen optimization. - VRR
behavior. - Multi-monitor refresh-rate behavior. - VSync and tearing
policy. - Focus and input handling. - Game capture and overlays. - HDR
pipeline if supported. - Compositor effects during fullscreen games.

Provide a temporary gaming compositor policy that can reduce expensive
desktop effects when a game is fullscreen, but: - Do not disable effects
blindly. - Do not cause visible flicker or abrupt transitions. - Restore
the previous state after exit. - Respect user preferences. - Do not
disable accessibility effects or essential visual feedback.

## 9.3 VRR and refresh rate

Detect: - Display refresh rates. - Adaptive Sync/FreeSync/G-SYNC
capability. - Whether VRR is actually active. - Per-monitor behavior. -
Compositor and driver limitations.

Avoid forcing a global VSync or VRR setting that conflicts with the
game, compositor or Gamescope.

## 9.4 Frame caps

A stable cap can improve frame pacing, thermals and latency depending on
the game and display.

Provide optional: - In-game frame cap preference. - Gamescope cap. -
Driver-level cap where supported. - MangoHud cap only for
diagnostic/testing use.

Do not stack multiple frame limiters. Identify one owner and show the
active cap in diagnostics.

## 9.5 HDR

Treat HDR as a complete pipeline feature, not a toggle.

Validate: - Monitor EDID and HDR capability. - GPU/driver. - Kernel DRM
support. - Wayland compositor. - Gamescope or desktop HDR support. -
Color space and output mode. - Capture/overlay behavior.

If the full path is not supported, clearly mark HDR unavailable. Never
claim HDR merely because the display advertises it.

------------------------------------------------------------------------

# 10. Shader compilation and stutter reduction

Shader compilation stutter is a major Linux gaming pain point.

Investigate: - Mesa shader cache. - Steam shader pre-caching. - DXVK
state cache. - VKD3D-Proton cache. - Pipeline cache behavior. -
Background shader processing. - Disk space and cache cleanup. - Cache
corruption recovery.

Rules: - Do not delete caches automatically during normal game
startup. - Do not run heavy shader compilation at high priority while a
game is actively rendering. - Schedule background work intelligently. -
Allow users to pause/resume shader processing. - Keep cache locations
and ownership correct. - Avoid cache growth without limits. - Do not
disable Steam shader pre-caching by default.

Test first-run stutter and repeat-run behavior separately. Report
whether stutter is shader compilation, storage, CPU contention, GPU
saturation or an unrelated cause where telemetry allows.

------------------------------------------------------------------------

# 11. Memory, storage and I/O

## 11.1 Memory policy

Support systems with 8 GB RAM.

Investigate: - zram configuration. - zswap compatibility. - systemd-oomd
or another coherent OOM policy. - `vm.max_map_count`. - Memory pressure
monitoring. - Game and desktop memory usage. - Shader cache limits. -
Browser/background app memory behavior.

Do not disable swap globally. Do not enable both zram and zswap in a
conflicting configuration without understanding the intended swap
topology.

Choose a conservative zram policy based on RAM size and compression
support. Measure CPU overhead and responsiveness.

Do not set `vm.min_free_kbytes`, `vm.watermark_scale_factor`,
`vm.swappiness`, MGLRU or THP settings from internet recipes without
testing. These affect allocation behavior and can cause regressions on
low-memory machines.

## 11.2 Storage

Support: - NVMe. - SATA SSD. - HDD. - External game drives. - Btrfs and
ext4 where DaisyOS supports them.

Investigate: - Correct I/O scheduler selection for each device. -
TRIM/discard policy. - Mount options. - Btrfs compression and CoW
implications for game libraries. - Filesystem fragmentation and free
space. - Shader cache placement. - Steam library permissions.

Do not force one I/O scheduler for every device. Do not disable
journaling or use unsafe mount options for gaming.

Avoid moving game libraries or caches without the user's explicit
consent.

## 11.3 Background I/O

During gameplay: - Avoid aggressive indexing. - Avoid unnecessary
package database operations. - Avoid scheduled maintenance that can be
deferred. - Do not stop security-critical or essential system
services. - Do not pause user-requested backups without clear policy and
user consent.

Use I/O scheduling and service policy conservatively. Ensure background
work resumes afterward.

------------------------------------------------------------------------

# 12. Interrupts, networking, audio and input latency

## 12.1 IRQ management

Investigate IRQ distribution and `irqbalance` behavior.

Do not disable irqbalance by default. Do not pin all interrupts away
from the game or onto one core.

Only create an IRQ policy if: - The device and IRQ topology are
understood. - The target system is tested. - The policy is reversible. -
Frame-time or latency improvements are repeatable. - Audio, networking
and input remain stable.

## 12.2 Networking

For online gaming: - Use the correct network driver and firmware. -
Avoid background downloads during gameplay when the user enables a
gaming network policy. - Provide optional download bandwidth limits. -
Do not claim CPU scheduler tuning fixes network latency. - Do not force
TCP congestion control changes for ordinary game traffic. - Do not
disable firewall/security protections. - Preserve Wi-Fi power management
defaults unless a measured issue exists. - Test Ethernet and Wi-Fi
separately.

Avoid system-wide `BBR`, buffer, MTU or queue discipline changes as
generic gaming tweaks. Most game traffic is latency-sensitive UDP;
measure actual behavior.

## 12.3 Audio

Keep PipeWire/WirePlumber or the actual DaisyOS audio stack responsive.

-   Do not let a game starve audio threads.
-   Preserve low-latency audio configuration for normal desktop use.
-   Test Bluetooth, USB, HDMI and analog output.
-   Test voice chat and microphone input.
-   Avoid arbitrary real-time priorities.
-   Ensure game profile activation does not cause audio dropouts.

## 12.4 Input

Test: - USB and Bluetooth controllers. - Keyboard/mouse. - High
polling-rate mice. - Gamepads. - Hotplug. - Steam Input. - DaisyOS
Continuity/input features if enabled.

Do not sacrifice input reliability to improve a synthetic latency
metric.

------------------------------------------------------------------------

# 13. Laptop and handheld behavior

Gaming mode must understand power source and thermal limits.

## 13.1 AC power

When plugged in: - Use a performance-oriented policy within firmware and
thermal limits. - Allow CPU/GPU boost where supported. - Avoid
unnecessary power-saving limits. - Monitor thermal throttling and power
limits.

## 13.2 Battery power

On battery: - Use a balanced or user-selected policy. - Avoid silently
draining the battery. - Explain when maximum performance will reduce
battery life. - Allow explicit override. - Restore the selected battery
policy when the game exits.

## 13.3 Thermal management

Never disable firmware or kernel thermal protection.

Monitor: - CPU temperature. - GPU temperature. - Fan behavior where
supported. - Thermal throttling. - Power limits. - Battery temperature
where available.

If a system is thermally throttling, explain that more aggressive CPU
settings cannot create additional cooling capacity.

## 13.4 Handhelds

Treat handheld support as a separate validation target.

Investigate: - Controller integration. - Screen rotation. - TDP
limits. - Fan curves only through safe vendor-supported interfaces. -
Gamescope session. - Suspend/resume. - Battery and performance
overlays. - Per-game profiles.

Do not apply desktop-PC assumptions to handhelds.

------------------------------------------------------------------------

# 14. Overlays, monitoring and diagnostics

## 14.1 MangoHud

Offer MangoHud as an optional overlay.

Useful metrics: - FPS. - Frame time. - 1% lows where supported. - GPU
utilization. - GPU clock and temperature. - VRAM usage. - CPU
utilization and frequency. - RAM usage. - Power consumption where
supported.

Do not force an overlay on users. It can introduce overhead or
compatibility issues.

## 14.2 GOverlay

Offer GOverlay as an optional configuration UI if its packaging and
maintenance are suitable.

## 14.3 Diagnostics report

Provide a readable report that includes:

``` text
DaisyOS Gaming Diagnostics
--------------------------
Kernel:
CPU:
CPU frequency driver:
CPU power profile:
Scheduler:
GPU:
Kernel GPU driver:
Mesa/NVIDIA driver:
Vulkan 64-bit:
Vulkan 32-bit:
Steam:
Proton:
GameMode:
Gamescope:
MangoHud:
Session type:
Compositor:
Display refresh:
VRR:
Power source:
Thermal throttling:
Known warnings:
```

Include version numbers and actual detected values. Do not expose
private paths, usernames, serial numbers or unrelated personal data in a
shareable report.

## 14.4 Explainability

For every active optimization, show: - What it changes. - Why it is
active. - Which component requested it. - Which hardware it applies
to. - Whether it is experimental. - Whether it is currently working. -
How to turn it off.

------------------------------------------------------------------------

# 15. Compile-time and package repository optimization

This is a longer-term project and must not delay correct drivers or
gaming compatibility.

## 15.1 Package tiers

Maintain a generic baseline and optional architecture-optimized
packages.

Possible targets: - Generic x86-64 baseline. - x86-64-v2 if the package
ecosystem and baseline justify it. - x86-64-v3. - x86-64-v4. - Carefully
selected AMD Zen-specific packages only when supported and maintained.

The exact target tiers must be verified against the project's declared
hardware support.

## 15.2 Build flags

Evaluate: - `-O2` versus `-O3`. - LTO. - PGO. - AutoFDO. -
architecture-specific tuning. - linker choices. - ThinLTO where
supported.

Do not assume `-O3` always produces faster games. It can increase code
size and cache pressure.

Do not use `-march=native` in distributable packages.

## 15.3 Package selection

Prioritize testing on performance-sensitive components: - Kernel. -
Mesa. - Wine/Proton-related components where DaisyOS builds them. -
Compiler/runtime libraries. - Compression libraries. - Common multimedia
libraries.

Do not rebuild the entire distribution before measuring whether
optimized builds produce meaningful improvements.

## 15.4 Repository operations

Create: - Reproducible build definitions. - Signed packages. - Package
provenance. - Build logs. - Rollback versions. - CI tests for CPU
baseline compatibility. - Clear update channels. - Security update
policy.

Never make users choose between performance and receiving timely
security fixes.

------------------------------------------------------------------------

# 16. Rust implementation requirements for `daisy-gamingd`

Use the existing DaisyOS Rust conventions. Do not introduce a new
framework unnecessarily.

## 16.1 Suggested internal modules

``` text
daisy-gamingd/
  src/
    main.rs
    config/
    hardware/
      cpu.rs
      gpu.rs
      display.rs
      power.rs
      capabilities.rs
    detection/
      steam.rs
      process.rs
      session.rs
    profiles/
      mod.rs
      balanced.rs
      gaming.rs
      efficiency.rs
      policy.rs
    actions/
      cpu.rs
      scheduler.rs
      compositor.rs
      gamemode.rs
      gpu.rs
      memory.rs
    state/
      snapshot.rs
      transaction.rs
      restore.rs
    monitoring/
      thermal.rs
      power.rs
      frame_session.rs
    dbus/
    security/
    diagnostics/
    tests/
```

Adapt this layout to the actual workspace.

## 16.2 Design principles

-   Use typed capability structures.
-   Use explicit state machines.
-   Make profile activation idempotent.
-   Make restoration idempotent.
-   Validate all input.
-   Use structured logs.
-   Avoid shelling out when a stable library or kernel API exists.
-   If invoking system tools is necessary, use fixed executable paths,
    argument arrays and no shell interpolation.
-   Apply timeouts to external commands.
-   Handle missing sysfs files and hotplug.
-   Use async tasks only where they simplify concurrency.
-   Avoid busy polling.
-   Use event-driven D-Bus/systemd/udev integration where practical.
-   Keep the daemon's memory and CPU overhead very low.

## 16.3 Privilege and security

The daemon may need privileged access, but its API must be narrow.

-   Use system D-Bus policy and polkit for user-authorized changes.
-   Do not allow arbitrary file writes.
-   Do not allow arbitrary command execution.
-   Do not accept arbitrary kernel parameter strings.
-   Use a fixed allow-list of supported actions and values.
-   Validate the requesting user/session.
-   Keep game detection separate from privileged action execution.
-   Log security-relevant decisions.
-   Do not trust process names or environment variables as
    authorization.

## 16.4 D-Bus API

Design a versioned interface for: - GetStatus. - GetCapabilities. -
ListProfiles. - GetActiveProfile. - SetUserProfile. -
GetActiveGameSessions. - StartManualGamingSession. -
StopManualGamingSession. - GetOptimizationReport. - ResetToDefaults.

Do not expose raw sysfs paths or arbitrary command strings to the shell.

## 16.5 Configuration

Use a documented configuration schema.

Separate: - System defaults. - Hardware-derived defaults. - User
preferences. - Per-game overrides. - Experimental options.

User configuration must take precedence over automatic defaults where
appropriate.

A profile must be explainable and serializable for diagnostics.

------------------------------------------------------------------------

# 17. DaisyOS Settings and user experience

The feature should feel like part of DaisyOS, not a pile of Linux
utilities.

## 17.1 Gaming settings page

Sections:

1.  Gaming performance profile.
2.  Detected hardware and driver status.
3.  Steam and Proton.
4.  Game compatibility tools.
5.  Display, VRR and Gamescope.
6.  Performance overlay.
7.  Per-game settings.
8.  Laptop power behavior.
9.  Diagnostics and troubleshooting.
10. Advanced/experimental options.

## 17.2 Per-game profile

Allow a user to set: - Proton version preference (where Steam
integration supports it). - Gamescope enablement. -
Resolution/scaling. - Frame cap. - MangoHud. - GameMode request. - GPU
preference on hybrid systems. - Environment variables from a validated,
user-owned configuration field. - Optional compatibility overrides.

Do not put dangerous system-level kernel controls in per-game settings.

## 17.3 Automatic behavior

When a game starts: - Identify the session. - Determine the current user
power source and profile. - Validate available capabilities. - Apply the
selected safe profile. - Show a subtle status indication if desired. -
Avoid disruptive notifications or UI animations during gameplay.

When the game exits: - Restore previous state. - Return desktop effects
and profile to the user's preference. - Resume deferred background work
according to policy.

## 17.4 Accessibility and control

-   Keyboard navigation.
-   Screen reader labels.
-   Clear status text.
-   No color-only status indicators.
-   Simple defaults.
-   Advanced settings hidden behind an explicit advanced section.
-   Every automatic behavior can be disabled or explained.

------------------------------------------------------------------------

# 18. Benchmarking: prove every optimization

No performance feature is complete without reproducible evidence.

## 18.1 Benchmark environments

Compare at minimum: 1. Standard Arch Linux baseline. 2. CachyOS
baseline. 3. DaisyOS default profile. 4. DaisyOS gaming profile. 5.
DaisyOS experimental profile, if applicable.

Use identical: - Hardware. - BIOS/firmware settings. - GPU driver
version where possible. - Kernel version where possible, with kernel
differences documented. - Game build. - Proton version. - Resolution. -
Graphics settings. - Display refresh. - Background applications. - Power
source. - Thermal starting conditions.

Record every deviation.

## 18.2 Representative hardware matrix

Include:

### Older Intel

-   i7-3770K-class system.
-   No AVX2.
-   Older NVIDIA GPU if available.
-   8 GB RAM test.

### Modern Intel

-   Hybrid P/E-core CPU.
-   Integrated and discrete GPU configurations.

### AMD desktop

-   Ryzen Zen 2 or newer.
-   RDNA GPU.
-   RADV.

### NVIDIA desktop

-   Supported GeForce GPU.
-   Proprietary driver.
-   Vulkan and Proton.

### Laptop

-   Hybrid graphics.
-   AC and battery tests.
-   Suspend/resume.
-   Thermal limits.

### Low-memory machine

-   8 GB RAM.
-   SATA SSD or HDD where possible.
-   Browser and desktop background load.

Do not claim broad hardware support based on one desktop PC.

## 18.3 Games and workloads

Use a repeatable set covering: - CPU-bound game. - GPU-bound game. -
DirectX 11 via DXVK. - DirectX 12 via VKD3D-Proton. - Native Vulkan
game. - Native OpenGL game if relevant. - Shader-heavy game. -
Multiplayer game. - Game with launcher/anti-cheat constraints where
legally testable.

Use a fixed benchmark scene, replay or built-in benchmark. Record the
exact scene and game version.

## 18.4 Metrics

Record: - Average FPS. - Median FPS. - 1% low. - 0.1% low. - Frame-time
distribution. - Frame-time variance. - Stutter count and duration. -
Input latency where measurable. - CPU utilization and effective
frequency. - GPU utilization, clocks, temperature and power. - VRAM and
system RAM. - Disk I/O. - Power consumption. - Thermal throttling. -
Game launch time. - Shader cache warm/cold behavior.

Use MangoHud and suitable frame-time capture tools. Validate the
accuracy and limitations of each metric.

## 18.5 Statistical process

-   Run each test multiple times.
-   Warm up the system.
-   Control background load.
-   Use the median of repeated runs.
-   Preserve raw logs.
-   Report run-to-run variance.
-   Reject results contaminated by thermal throttling or background
    updates.
-   Do not compare results from different game builds without
    disclosure.
-   Use a practical minimum improvement threshold before enabling a
    tweak by default.

A small FPS difference within measurement noise is not proof of an
optimization.

## 18.6 Acceptance criteria

A performance change may become default only if: - It improves a
meaningful target metric on a representative hardware group. - It does
not create unacceptable regressions in other groups. - It does not harm
stability, audio, input, networking or desktop usability. - It has a
clear fallback. - The improvement is repeatable. - Its maintenance cost
is justified.

A change that improves average FPS but materially worsens 1% lows should
not be accepted without a clear, documented trade-off.

------------------------------------------------------------------------

# 19. Reliability, recovery and update safety

Gaming optimizations must never leave a system in a broken state.

## 19.1 Kernel update safety

-   Keep a known-good kernel.
-   Keep a working bootloader entry.
-   Ensure initramfs generation succeeds.
-   Ensure NVIDIA modules match the new kernel.
-   Verify DKMS/prebuilt module status before reboot.
-   Preserve old kernel packages for rollback according to storage
    policy.
-   Test interrupted updates.
-   Never remove the fallback kernel before the new kernel has booted
    successfully.

## 19.2 Profile recovery

-   Store original values before changes.
-   Use transaction logs for profile activation.
-   Restore settings after daemon crash or game crash.
-   Add a boot-time reconciliation service if required.
-   Do not restore stale settings over a user's newer preference.
-   Provide a safe reset command and Settings action.

## 19.3 Suspend and resume

Test: - Game running during suspend. - Game paused during suspend. -
Suspend while gaming profile active. - Resume with AC removed. - Resume
with external monitor removed. - GPU hotplug and hybrid switching where
supported. - Scheduler restart after resume.

Re-detect hardware state after resume; do not assume sysfs values remain
valid.

## 19.4 Safe mode

Provide a way to boot without: - Experimental scheduler. - Experimental
kernel parameters. - Automatic gaming profile. - Optional GPU tuning.

The fallback must remain usable even if the gaming daemon is broken.

------------------------------------------------------------------------

# 20. Testing and CI

## 20.1 Unit tests

Test: - CPU feature parsing. - GPU capability parsing. - Profile policy
selection. - Unsupported sysfs values. - State snapshots. - State
restoration. - Partial activation failure. - Game detection. - Multiple
sessions. - User override precedence.

## 20.2 Integration tests

Use test doubles or isolated environments for: - CPU sysfs. - GPU
sysfs. - systemd service state. - D-Bus. - GameMode. - Scheduler
loader. - Power profiles. - Suspend/resume events.

Do not require real gaming hardware for every unit test.

## 20.3 Hardware tests

Maintain a manual/automated test matrix for: - AMD. - Intel. - NVIDIA. -
Hybrid laptops. - Older CPU without AVX2. - 8 GB RAM. - Multiple
displays. - Wayland and X11 where supported.

## 20.4 Regression tests

Every release should verify: - Normal desktop idle. - App launch. -
Steam launch. - Game launch and exit. - Profile restoration. - Audio
playback. - Controller input. - Suspend/resume. - Network
connectivity. - Driver and Vulkan health. - Fallback kernel boot.

------------------------------------------------------------------------

# 21. Licensing, provenance and maintenance

DaisyOS may adopt ideas or code from CachyOS and other projects, but
must do so correctly.

-   Review the license of every patch, script, package and code
    component.
-   Preserve required copyright and attribution notices.
-   Do not copy code without checking its license.
-   Track upstream source and commit.
-   Prefer packaging upstream projects over forking them.
-   Maintain a patch inventory.
-   Keep security updates flowing.
-   Do not redistribute proprietary NVIDIA components outside their
    license terms.
-   Respect Proton-GE and other project distribution policies.
-   Avoid depending on abandoned tools without a maintenance plan.

Create an upstream tracking document for: - CachyOS kernel. - Linux
kernel. - Mesa. - NVIDIA driver. - Proton. - DXVK. - VKD3D-Proton. -
Gamescope. - GameMode. - MangoHud. - sched_ext / scx. -
power-profiles-daemon.

------------------------------------------------------------------------

# 22. Implementation phases

Do not attempt all phases at once. Each phase must end with a working,
testable system.

## Phase 0 --- Repository audit and baseline

Deliverables: - Repository architecture audit. - Current package
inventory. - Current hardware support matrix. - Baseline gaming
diagnostics. - Baseline benchmarks on available hardware. - Identified
risks and ownership map.

Exit criteria: - Codex can explain how DaisyOS builds, installs, boots,
updates and configures its current kernel and desktop. - No speculative
changes have been made.

## Phase 1 --- Correct gaming foundation

Implement: - Multilib strategy. - Steam package integration. - Correct
AMD/Intel/NVIDIA graphics packages. - 32-bit graphics libraries. -
Vulkan loader and ICD validation. - Firmware dependencies. - Diagnostic
command/report.

Exit criteria: - Correct GPU detected. - 64-bit and 32-bit Vulkan
verified. - No software rendering on supported GPU configurations. -
Steam starts and launches a test game.

## Phase 2 --- Compatibility stack

Implement: - Proton support. - Proton Experimental option. - Optional
Proton-GE integration. - DXVK/VKD3D validation. - NTSYNC detection and
supported integration. - UMU evaluation for non-Steam games. -
Controller and overlay validation.

Exit criteria: - Native Vulkan, DX11 and DX12 test paths work where
supported. - No forced experimental runtime. - Compatibility limitations
are clearly reported.

## Phase 3 --- Gaming tools

Implement: - GameMode. - Gamescope optional integration. - MangoHud
optional integration. - GOverlay optional packaging. - Settings page for
tools and per-game options.

Exit criteria: - Users can enable/disable each tool. - Games launch
without mandatory wrappers. - No conflicting frame limiters. - No
duplicate priority managers.

## Phase 4 --- CPU and power profiles

Implement: - CPU capability detection. - Intel/AMD driver detection. -
EPP/governor capability detection. - AC/battery-aware profiles. - Safe
temporary profile changes. - State snapshot and restoration.

Exit criteria: - Gaming profile activates and restores correctly. -
Unsupported hardware is handled gracefully. - Desktop remains
responsive. - No thermal protection is bypassed.

## Phase 5 --- `daisy-gamingd`

Implement: - Rust daemon. - D-Bus API. - Session state machine. -
Steam/game detection. - Profile transaction engine. - Diagnostics and
structured logs. - Polkit authorization. - Crash recovery.

Exit criteria: - Game start and exit are detected reliably. - Partial
failures roll back. - Daemon restart does not leave stale settings. -
Unprivileged game processes cannot request arbitrary privileged actions.

## Phase 6 --- Gaming kernel

Implement: - Separate gaming kernel package. - CachyOS patch/config
research. - Scheduler experiments. - Preemption/tick-rate benchmark
matrix. - Kernel fallback and update safety. - NVIDIA module
compatibility tests.

Exit criteria: - Both default and gaming kernels boot. - No single
experimental patch is required. - Benchmark evidence exists for every
default kernel difference. - Recovery path is tested.

## Phase 7 --- Advanced scheduling and system tuning

Evaluate independently: - BORE. - sched_ext candidates. - POC selector
where applicable. - IRQ policy. - CPU affinity. - Hybrid-core
placement. - Memory policies. - I/O scheduler policy. - Shader
compilation scheduling.

Exit criteria: - Only reproducibly beneficial options become defaults. -
Experimental options remain opt-in. - Every setting has a rollback path.

## Phase 8 --- DaisyOS desktop gaming integration

Implement: - Fullscreen detection. - Temporary compositor policy. - VRR
and refresh diagnostics. - Gamescope UI. - Display and frame-cap
policy. - Seamless profile transitions. - Restoration of desktop
effects.

Exit criteria: - Gaming does not break desktop navigation. -
Multi-monitor behavior is tested. - VRR and refresh status are
truthful. - No visual flicker or persistent desktop setting changes.

## Phase 9 --- Package optimization

Implement: - Generic baseline. - CPU feature detection. - Optional
architecture-specific package repository. - Reproducible builds. -
LTO/PGO/AutoFDO experiments. - Security and update policy.

Exit criteria: - Older supported hardware remains compatible. -
Optimized packages are measurably beneficial. - Package provenance and
rollback work.

## Phase 10 --- Release quality and continuous benchmarking

Implement: - Benchmark suite. - Hardware matrix. - CI tests. -
Regression reports. - User diagnostics export. - Release checklist. -
Documentation and troubleshooting.

Exit criteria: - DaisyOS Gaming release has repeatable results against
the documented baseline. - All critical recovery and compatibility tests
pass. - No unverified optimization is enabled by default.

------------------------------------------------------------------------

# 23. Definition of done

DaisyOS Gaming is ready for general release only when all of the
following are true:

## Hardware and drivers

-   [ ] AMD, Intel and NVIDIA detection works.
-   [ ] Correct driver and firmware are installed.
-   [ ] 64-bit Vulkan works.
-   [ ] 32-bit Vulkan works.
-   [ ] Hybrid GPU selection is documented and tested.
-   [ ] Software rendering is detected and reported.
-   [ ] NVIDIA kernel module/update compatibility is tested.

## Gaming compatibility

-   [ ] Steam installs and launches.
-   [ ] Proton stable works.
-   [ ] Proton Experimental can be selected.
-   [ ] Proton-GE is optional.
-   [ ] DXVK path is tested.
-   [ ] VKD3D-Proton path is tested.
-   [ ] NTSYNC is capability-detected.
-   [ ] Controllers work.
-   [ ] Anti-cheat limitations are clearly communicated.

## Performance

-   [ ] GameMode works.
-   [ ] Gaming profile is reversible.
-   [ ] CPU policy is hardware-aware.
-   [ ] GPU policy is vendor-aware.
-   [ ] Frame-time metrics are recorded.
-   [ ] 1% and 0.1% lows are measured.
-   [ ] Shader cache behavior is validated.
-   [ ] Background services do not create avoidable stutter.
-   [ ] No unsafe universal tuning is enabled.

## Desktop integration

-   [ ] DaisyOS remains responsive during gameplay.
-   [ ] Fullscreen and focus transitions work.
-   [ ] Gamescope is optional.
-   [ ] MangoHud is optional.
-   [ ] VRR status is accurately reported.
-   [ ] Multi-monitor behavior is tested.
-   [ ] Desktop effects restore after gaming.

## Reliability

-   [ ] Default kernel remains available.
-   [ ] Gaming kernel has a fallback.
-   [ ] Profile restoration works after crashes.
-   [ ] Suspend/resume works.
-   [ ] Updates preserve a bootable system.
-   [ ] Safe mode works without experimental tuning.
-   [ ] No privileged arbitrary command execution exists.

## Low-end support

-   [ ] Generic CPU baseline is preserved.
-   [ ] i7-3770K-class CPU does not receive AVX2-only binaries.
-   [ ] 8 GB RAM configuration is tested.
-   [ ] Gaming profile does not create excessive idle overhead.
-   [ ] Battery and thermal behavior are acceptable.

------------------------------------------------------------------------

# 24. Upstream references to consult

These are starting points, not substitutes for checking the exact
version and package available when implementing.

## CachyOS

-   Kernel overview: https://wiki.cachyos.org/features/kernel/
-   sched_ext guide: https://wiki.cachyos.org/configuration/sched-ext/
-   Kernel source: https://github.com/CachyOS/linux-cachyos
-   CachyOS wiki: https://wiki.cachyos.org/

## Arch Linux

-   Gaming: https://wiki.archlinux.org/title/Gaming
-   GameMode: https://wiki.archlinux.org/title/GameMode
-   Gamescope: https://wiki.archlinux.org/title/Gamescope
-   Steam: https://wiki.archlinux.org/title/Steam
-   Vulkan: https://wiki.archlinux.org/title/Vulkan
-   NVIDIA: https://wiki.archlinux.org/title/NVIDIA
-   CPU frequency scaling:
    https://wiki.archlinux.org/title/CPU_frequency_scaling

## Upstream projects

-   Linux kernel documentation: https://docs.kernel.org/
-   Valve Proton: https://github.com/ValveSoftware/Proton
-   DXVK: https://github.com/doitsujin/dxvk
-   VKD3D-Proton: https://github.com/HansKristian-Work/vkd3d-proton
-   Gamescope: https://github.com/ValveSoftware/gamescope
-   GameMode: https://github.com/FeralInteractive/gamemode
-   MangoHud: https://github.com/flightlessmango/MangoHud
-   sched_ext schedulers: https://github.com/sched-ext/scx
-   power-profiles-daemon:
    https://gitlab.freedesktop.org/upower/power-profiles-daemon
-   Mesa: https://docs.mesa3d.org/

------------------------------------------------------------------------

# 25. Final instruction to Codex

The goal is not to make DaisyOS look optimized by adding a large number
of kernel parameters, services and scripts.

The goal is to build a **measurably fast, stable, intelligent and
maintainable gaming platform**.

Start with Phase 0. Inspect the actual DaisyOS repository and produce
the audit before writing implementation code.

Then implement one phase at a time, in dependency order. For every
proposed optimization:

1.  Explain the bottleneck it is intended to solve.
2.  Identify which hardware and workloads it applies to.
3.  Verify the upstream interface and current implementation.
4.  Establish a reproducible baseline.
5.  Implement it behind a safe feature flag or profile.
6.  Benchmark it against the baseline.
7.  Test stability, desktop responsiveness and rollback.
8.  Enable it by default only when the evidence supports doing so.
9.  Document the decision and retain the raw results.

**Do not stop at installing gaming packages. Build the hardware
detection, policy, lifecycle management, diagnostics, UI integration and
benchmark infrastructure that make those packages work together as one
coherent DaisyOS experience.**

The final result should make a fresh DaisyOS installation ready for
gaming without requiring the user to manually discover drivers,
configure Vulkan, hunt for Proton versions, tune CPU settings or
troubleshoot common Linux gaming problems.
