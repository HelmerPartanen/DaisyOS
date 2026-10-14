# Everyday reliability audit — 2026-10-08

This is a repository-wide automated gate, targeted concurrency/resource review, and read-only check of the running native session. It does not certify all hardware configurations or reliability at hundreds of thousands of installations.

## Changes

- External commands now have bounded stdout/stderr capture (8 Mi characters per stream by default). Excess output terminates the owned process and returns an explicit failure, rather than consuming memory indefinitely. Caller cancellation and argument-safe execution remain supported.
- Text persistence uses unique same-directory temporary files and atomic replacement. Migrated appearance, desktop layout, session, Calendar, Files preferences, language environment, GTK theme, and KWin personalization configuration. Concurrent saves cannot share a temporary filename or expose a partially written final document. This is not an fsync-based guarantee against power loss.
- Wallpaper source decoding is shared through a leased LRU cache, bounded to three cached sources and 128 MiB of decoded pixels. Active leases protect images from eviction/disposal during rendering. Oversized/retired active images and rendered outputs are outside that cache budget. File timestamp/length changes invalidate source entries; failures are retried.
- Concurrent requests for the same wallpaper metrics share generation instead of cancelling one another. Cancelled or failed renders release their owned target; disposed services reject late publication. Previously displayed outputs remain alive until the owning service retires, preserving fade/render safety.
- Wallpaper tests now run with an initialized Skia/Avalonia rendering platform. They assert actual non-null images, rather than passing through null decode results. Added concurrency, cache eviction, file replacement, bounded output, and atomic-write coverage.
- A launcher rendering-test wait now pumps the UI dispatcher until the loading task completes. The earlier helper could stop pumping and time out while waiting for UI publication; one full-gate rerun exposed this failure.
- The toggle intermediate-animation test samples render ticks directly: screenshot capture can drain a short animation before its intermediate assertion. Duration and final position/color assertions are retained.
- Timezone-to-country tables are loaded once instead of repeatedly reading zoneinfo files on clock ticks.
- Avalonia designer tooling supports `BunExecutable` and uses `dotnet tool exec` when unspecified, avoiding reliance on an absent `dnx` launcher. On this host the packaged Bun tool exited with an illegal instruction; validation used the official standalone Bun executable. See [Bun installation documentation](https://bun.sh/docs/installation).

## Validation

Run the complete gate with an available Bun executable:

```sh
BunExecutable=/path/to/bun scripts/test.sh
```

Full gate: 682 tests passed, one skipped (Core 390, Shell 122, rendering/UI 170). Release scripts, ISO work-tree guards, production publishing, KWin repository contract, packaging/branding, native development-session installer, compositor session checker, and ISO profile checks passed. The skipped case is the opt-in live KWin screenshot capture test. Existing compiler/analyzer warnings remain; this is not a warning-free build claim.

The strict compositor-session check passed all nine requirements with zero warnings: Wayland session/socket/round-trip, installed/running KWin, XWayland, shell, desktop portal, and PipeWire.

After the final gate, the native development restart rebuilt Files, Notes, and Settings with zero warnings/errors and replaced the shell. KWin retained the same process; the new shell runs with `--real-services --shell-session`. All nine strict compositor checks passed again, and there were no failed user services. This verifies startup/session health, not every interactive workflow.

Tests use temporary owned files and process fixtures. No live Trash emptying, logout, disk formatting, OS upgrade, or network reconfiguration was performed.

| Area | Evidence | Remaining acceptance |
| --- | --- | --- |
| Files, navigation, localization, controls, theme and layout | Full application/core/UI suites | Physical display appearance, unusual fonts/languages, external applications |
| Persistence | Concurrent atomic replacement, cancellation, failed commit tests | Power-loss/fsync durability and long-term recovery |
| Wallpaper and image lifetime | Real renderer, shared generation, eviction leases and replacement tests | GPU/driver diversity and long-running transitions |
| Command execution | Argument, cancellation, timeout and excessive-output tests | Platform/service failure diversity |
| Native session | Network connected, PipeWire available, time synchronized, no failed user services | Suspend/resume, authentication/logout, hardware unplug/replug |
| Wi-Fi and Bluetooth | Wi-Fi hardware absent; Bluetooth read-only probe did not complete | Real adapters, pairing and reconnect workflows |
| Packaging/session scripts | Full repository gate | Clean installed-system upgrades and rollback on representative machines |

## Performance observations and limits

The running shell sampled over 20 seconds consumed approximately 20.3% of one CPU core, 723 MiB RSS and 476 MiB PSS. A separate thread sample concentrated CPU time in the Wayland worker and UI thread. This was a currently used desktop, not a controlled idle benchmark. Managed tracing showed native rendering/presentation work but does not establish its precise CPU cause. These observations do not justify a measured before/after improvement claim.

Review also confirmed existing limits for notification history, file metadata/visual work, directory caches, thumbnails, window retention and prefetching. Those paths were retained rather than weakening behavior to reduce apparent memory use.

Further release acceptance needs native CPU profiling, controlled idle/active benchmarks, memory plateau/soak measurements, multiple resolutions/scales and GPU drivers, suspend/resume, multi-monitor/hotplug, accessibility and language/font matrices, disk-full and service-failure recovery, and installed-system upgrade/rollback trials. Population-wide reliability needs staged deployment and observed failure rates; one development machine and automated suites cannot establish it.
