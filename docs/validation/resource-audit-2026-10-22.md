# DaisyOS resource audit and remediation plan — 2026-10-22

## Conclusion

There are two substantial costs: the development environment and DaisyOS itself.
The reported 1.5 GiB `dotnet` process is the Avalonia VS Code language server,
not an unidentified OS daemon. The shell also has a real, independently excessive
resident footprint: approximately 1.0 GiB proportional RAM and 694 MiB dedicated
GPU memory in this session. Development overhead does not excuse the shell cost.

This audit changes no running services or user preferences. It provides source
findings, live measurements, and an implementation plan. It does not claim that
RDR2's reported combined 15.4 GB use has been reproduced or fixed.

## Evidence and measurement limits

Read-only host sampling captured twelve approximately five-second intervals.
The session was already running with VS Code and other applications open. This
is an existing-workload sample, not a controlled idle desktop. No IDE, application,
wallpaper, compositor effect, or game was stopped to manufacture a lower result.

Artifacts: `artifacts/performance/2026-10-22-idle-audit/` contains process snapshots,
interval CPU/PSS samples, a summary, shell mapping totals, NVIDIA information,
display configuration, and cgroup counters. Process command lines in JSON were
reduced to executable and role to avoid retaining command-line tokens.

| Component | Final PSS | Final RSS | Mean CPU, percent of one core |
| --- | ---: | ---: | ---: |
| Avalonia VS Code language server, PID 356061 | 1,568.5 MiB | 1,675.0 MiB | 0.02% |
| DaisyOS.Shell, PID 324128 | 1,029.1 MiB | 1,073.3 MiB | 1.40% |
| C# project-system service host | 594.1 MiB | 707.1 MiB | 0.11% |
| Roslyn language server | 354.1 MiB | 446.5 MiB | 0.23% |
| Six persistent MSBuild workers, combined | 687.1 MiB | 1,239.9 MiB | approximately 0% |
| KWin | unavailable through smaps | initial ps RSS 433 MiB | 6.75% |

All identified Avalonia/C# development services and MSBuild workers total
3,502.5 MiB PSS (3.42 GiB). VS Code processes add about 1,624.2 MiB PSS.
These are observed process totals, not guaranteed savings from closing one window.
The earlier lifetime `%CPU` listing considerably overstates their current work;
interval sampling shows the large language server was effectively CPU idle.
Missing smaps data means unavailable, not zero memory.

Shell PSS ranged from 1,028.8 to 1,029.1 MiB: stable over this minute. This does
not establish whether earlier interactions caused retention or whether a longer
soak leaks. Its executable is `src/DaisyOS.Shell/bin/Debug/net10.0/DaisyOS.Shell`.
A Release comparison is required; Debug alone cannot explain away this footprint.

NVIDIA reports 694 MiB GPU allocation for the shell. Device utilization across
sampled readings ranges from 3–19%, mean 9.9%; device memory is 1,254–1,307 MiB.
A separate point sample attributed GPU engine activity to KWin. This does not
prove KWin originates the work: client damage, video, blur, overlays, and the IDE
can all cause compositor work. GPU memory and process PSS are different accounting
domains; do not add them blindly, especially on integrated graphics.

The host has 15.55 GiB kernel-reported RAM. Final MemAvailable was about 7.69 GiB,
so total-minus-available was about 7.86 GiB with these apps open. It has no active
swap. Memory PSI avg10/avg60 were zero at sample end, but avg300 was nonzero;
this sample does not establish absence of earlier pressure. The RDR2 failure
needs a capture during the actual game workload, including OOM and driver logs.

## Findings from the source audit

### 1. Shell memory is predominantly private resident memory — high priority

A separate mapping snapshot attributes approximately 734 MiB PSS to unnamed
anonymous mappings and 156 MiB to the native `[heap]`, with approximately 65 MiB
in `memfd:doublemapper`. About 816 MiB is backed by anonymous huge pages across
these groups. Shared library code is not the main explanation for the 1 GiB.

These categories do not identify allocation owners: anonymous mappings can hold
managed heaps, native allocations, thread stacks, and driver state. Doublemapper
mappings must not automatically be called image buffers. A fresh managed heap
inventory, committed-versus-live GC counters, and native allocation profiling are
necessary to distinguish retention from allocator/GC high-water marks.
The older audit in `docs/architecture/performance-and-memory.md` is useful history,
but its smaller managed heap measurement is not a measurement of today's process.

### 2. Rendering and surface ownership are the highest-value graphics investigation

`src/DaisyOS.Shell/Program.cs` permits a 256 MiB Skia GPU resource cache at this RAM
size, and forces the WSI/EGL route (`UseDmabufSwapchain = false`). The cache value
is a limit, not proof of 256 MiB currently allocated. The 694 MiB GPU allocation
exceeds that cache limit and requires inventory of active render targets,
swapchains, texture copies, video frames, and native driver allocations.

`App.axaml.cs` creates multiple native layer surfaces; some are deferred until
first use, and persistent roles preserve reopening behavior. Count configured,
mapped, and buffer-owning surfaces separately. Fullscreen transparent surfaces
must not keep screen-sized buffers or cause continuous damage without a need.
Do not destroy persistent roles blindly: the existing comments document native
Wayland remapping and first-frame regressions.

`external/Avalonia/src/Avalonia.Wayland/Server/WaylandWorker.RenderTimer.cs` has a
20 Hz starvation timer armed after composition commits. Verify that settled
scenes disarm it and that callbacks do not perpetuate commits. Its existence
alone does not prove an idle rendering bug.

`Services/Compositor/KWinBlur.cs` immediately rebuilds regions on layout updates,
then compares completed lists. The equality check avoids redundant protocol
updates but not the region-building allocations or ancestor scans. Profile this
path during widget/clock updates and animation. Preserve synchronization that
prevents blur-mask flashes when optimizing it.

### 3. Wallpaper has multiple independently owned memory costs

`WallpaperSourceCache.cs` keeps up to three full-resolution decoded sources,
with a 128 MiB cached-byte budget. Active leases can outlive eviction, and initial
decode occurs before budget enforcement. `WallpaperImageService.cs` also creates
service-owned output render targets. The source budget therefore is not a total
wallpaper-memory cap. For scale, one 3840×2160 RGBA image is 31.6 MiB before extra
buffers or GPU copies.

The mpv path can keep decoder/render resources even while paused. In
`Views/Components/Wallpaper/WallpaperLayer.axaml.cs`, occlusion, gaming, fullscreen,
and lock state already pause playback and stop frame polling. Check these paths
live rather than implementing a duplicate pause feature. Add delayed release of
expensive decoder/graphics state while preserving a poster and prompt resume.
Keep static wallpaper as the minimum-resource baseline. Video optimization's
5 GiB limit and HEIC's 512 MiB limit are disk-cache limits, not resident RAM.

### 4. UI and cache retention need byte budgets and pressure response

`Views/ShellView.axaml` eagerly constructs hidden launcher, console, settings,
metrics, and screenshot controls. Hidden views do not automatically release
models, decoded artwork, subscriptions, or native graphics resources. Existing
console work is partly visibility-gated, so measure construction cost separately
from recurring background activity.

`LauncherIconLoader.cs` uses an 896-entry thumbnail cache (roughly 56 MiB at
128×128 RGBA), but catalog-item leases can retain images beyond cache eviction.
`Apps/Files/Services/ThumbnailCache.cs` also accounts by entries, not bytes.
Bound total live decoded bytes, account for leases, decode to requested size,
and release offscreen images under pressure without discarding original artwork.

`DesktopActionReader.cs` has process-lifetime dictionaries without an explicit
capacity. `GameArtworkResolver.cs` retains resolved metadata by identity. These
are lower-priority retention risks, not demonstrated gigabyte causes.
Conversely, browser artwork and Spotify canvas metadata already have entry
limits; do not describe every concurrent dictionary as unbounded.

`Services/Windows/LightweightWindowCache.cs` permits multiple retained app windows
outside the native shell, but `App.axaml.cs` disables retention for the native
session. It is not a supported explanation for this live shell's memory.
Some apps still share shell lifetime: App Store retains `_appStoreSession` after
its window closes. Verify each in-process app's close/dispose behavior and move
large optional apps out of the shell if retained state dominates the profile.

### 5. Idle work is distributed and needs lifecycle-based suspension

- Desktop trash checks enumerate the trash directory on the UI thread every
  second (`DesktopView.axaml.cs`, `DesktopViewModel.RefreshTrashIcon`). Replace
  with scoped filesystem notifications and a slow recovery check.
- The system bar updates its clock every second and refreshes quick-settings
  status every 45 seconds. Refresh only changed fields; prefer service signals
  and cache backend results across consumers. Do not poll expensive commands for
  a hidden panel merely because a status indicator is present.
- `GamingModeAutomation.cs` scans for games every three seconds. Retain reliable
  detection, but measure procfs scanning cost and back off when no candidates
  exist. Resume detection promptly on window/process changes.
- The media spectrum updates every 40 ms during playback. It already has some
  visibility gating; ensure capture itself stops when covered or gaming and
  keep clocks/artwork controls usable without constant animation.
- `RecentAppPrefetcher.cs` can request 512 MiB readahead after idle, uses a fixed
  1 GiB available-memory threshold, and already suspends for gaming/fullscreen.
  Readahead is reclaimable page cache, not a 512 MiB managed allocation. Make it
  opt-in until matched launch tests justify it, and use proportional headroom
  plus memory/I/O pressure instead of the fixed threshold.

### 6. Existing diagnostics cannot attribute component resource use reliably

`Services/Diagnostics/ShellMetricsMonitor.cs` estimates component memory as
`elementCount * 0.008 + constant` and distributes CPU by visual-tree weights.
It does not measure component allocations or CPU execution. Label those fields
as estimates and replace diagnostic attribution with sampled stacks, allocation
stacks, render/surface counters, and explicit cache byte counters. Avoid recursive
visual-tree scans as a permanent performance-monitoring workload.

The gaming overlay's MEM number uses total RAM minus MemAvailable
(`LinuxSystemMetricsService.cs`), so it includes the whole running machine. It
is not the shell's footprint. Explain scope and use consistent GiB labels.

### 7. Installed-image policy and the running host differ

The archiso image configures zstd zram up to 4 GiB; `/proc/swaps` on this host is
empty. Shipping configuration is not proof that a development installation is
protected from sudden game memory demand. Verify zram and OOM policy in both
install paths. Compression provides pressure resilience; it does not replace
reducing retained memory or guarantee that a given game fits.

`scripts/daisyos-memory-policy.sh` reserves 2 GiB of headroom at this RAM tier.
The live shell service reports MemoryHigh=infinity, MemoryMin=512 MiB, and
MemoryCurrent about 2.07 GiB. Cgroup memory includes descendants and charged
cache, not just shell PSS. Inspect membership and `memory.stat`; this is not
proof of another 1 GiB shell leak. Audit app-scope routing before tuning limits.
Do not impose a small shell hard cap that turns pressure into desktop crashes.

## Ordered implementation plan

| Order | Work | Completion evidence |
| --- | --- | --- |
| P0: baseline | Add a published Release session route with no SDK/build-server startup; retain explicit development route. Inventory all service descendants. Capture 10-minute static and live-wallpaper baselines after two-minute settling. | Reproducible PSS/private/GC/native/GPU/CPU/frame counts; no language servers or MSBuild in production session. |
| P0: attribute | Capture GC live/committed size, allocation rate, top managed retainers, native allocation stacks, graphics resources, and active layer surfaces. Repeat after opening/closing apps and lock screen. | A byte-accounted explanation of the shell's approximately 1 GiB RAM and 694 MiB VRAM; unknown remainder explicitly reported. |
| P1: graphics | Remove idle frame producers; avoid oversized or duplicate surfaces; release unused render targets; compare 64/96/128/256 MiB cache settings on matched workloads. | Lower RAM/VRAM and idle submissions without first-open, blur, animation, HDR, capture, or multi-monitor regressions. |
| P1: wallpaper | Decode static assets to required display resolution; reduce redundant source/output copies; unload paused decoder state after a grace period; verify gaming pause live. | Measured per-resolution byte budgets, zero wallpaper frames while covered/gaming, correct and prompt resume. |
| P1: retention | Lazy-create expensive hidden views; dispose closed optional app state; byte-budget decoded images including live leases; add pressure-triggered reclamation. | 50 open/close cycles reach a plateau; hidden features show no recurring work; bounded peak decoded bytes. |
| P2: idle services | Replace trash polling; consolidate status providers; visibility-gate spectrum/telemetry; adapt game polling; disable default speculative prefetch pending evidence. | Per-service wakeup/CPU counts decrease; network/audio/Bluetooth/game detection remain reliable. |
| P2: pressure | Verify installed zram, app cgroups and OOM behavior; use PSI/headroom to shed optional caches and video state. | 4/8/16 GiB pressure tests keep shell/input working and report failures clearly. |
| P3: acceptance | Run identical RDR2 scenes with the same Proton, graphics settings, resolution, warmup, and overlays; capture per-process memory, GPU residency, PSI, storage stalls, and frametimes. | Lower OS overhead; no OOM; p99/1% lows and hitch counts improve or remain stable. |

Suggested engineering budgets below are targets to test, not achieved promises.
Start with a published, single-display 2560×1440 static desktop with no user apps:

- Shell PSS: first gate at or below 400 MiB; stretch target 250 MiB.
- Whole system total-minus-MemAvailable: at or below 1.5 GiB; stretch target
  1.0 GiB. Also publish process PSS and kernel/cache accounting separately.
- Shell dedicated VRAM: at or below 200 MiB at this display configuration;
  derive separate budgets for 4K, multiple displays, HDR, and live video.
- Settled shell CPU: at or below 0.5% of one core over ten minutes; combined
  desktop core at or below 2% of one core. No continuous shell frame submissions
  except explicitly active animation/video; clock updates may produce sparse frames.
- Device GPU activity: compare against a matched compositor-only baseline; aim
  for near-baseline activity and power on static desktop. A universal percentage
  is not meaningful across GPUs, refresh rates, and drivers.
- After 50 repeated app/launcher/lock cycles and settling: no continuing growth
  and no more than 10% over the warmed baseline. Record peaks and first-open
  latency so reclaiming memory does not hide unacceptable interaction costs.

For gaming, separately test fullscreen with overlays hidden/visible and automatic
Gaming Mode on/off. Require zero unnecessary shell video/spectrum rendering and
no speculative prefetch during play. Use `scripts/analyze-game-frametimes.py` with
matched MangoHud logs, and record loading/storage stalls separately from pacing.
A fixed game RAM cap is inappropriate: the executable, Proton, settings, and scene
also determine game memory demand.

## Recommended first implementation slice

Create the Release baseline and resource counters, then investigate graphics and
wallpaper ownership before rewriting the shell or changing runtime. The evidence
supports several GiB of separate development overhead plus a large private shell
allocation and a large GPU allocation. It does not yet support blaming .NET GC,
a particular widget, blur, or a specific memory leak for the whole cost.
