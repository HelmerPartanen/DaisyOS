# Performance and memory verification — 2026-10-14

This pass removes specific retained allocations and unnecessary work. It does
not establish a minimum possible footprint or certify every OS subsystem.

## Changes

- LocalSend uses an explicitly configured embedded host. It no longer loads
  application JSON configuration or recursively watches the process working
  directory. HTTPS routing, JSON request bodies, and LAN discovery remain available.
- Hidden console views defer game discovery, artwork requests, and settings
  queries. Their clock and discovery work stop while hidden.
- Console, diagnostics, and LocalSend native overlays map on first use. Their
  layer roles remain alive afterward so reopening does not recreate the role.
- Superseded static wallpaper render targets retire after the replacement
  reaches the render thread, instead of remaining until the wallpaper service closes.
- Video posters render in the short-lived `daisy-mpv-preview` helper. It uses
  the same native preview function and color conversion as before. Cancellation
  and a twenty-second deadline stop the exact child and remove its temporary
  pixel file. Playback still uses the native GPU child surface.
- Icon-generation inputs exclude generated `obj` and `bin` files, avoiding
  a dependency on the build's own outputs.
- Resource sampling now records PSS and private resident pages alongside RSS.

## Measurements and limits

The native development shell ran on the existing KWin session with the user's
live wallpaper, VS Code, and other applications open. The shell remained a
Debug build; standalone applications were built in Release.

| Measurement | Earlier inventory | Final inventory |
| --- | ---: | ---: |
| Shell filesystem watches | 120,031 | 1 |
| Captured live managed heap | 109,127,981 bytes (104.1 MiB) | 62,485,610 bytes (59.6 MiB) |
| Captured managed objects | 1,190,511 | 753,482 |

The managed heap inventory is about 43% smaller. It excludes graphics buffers,
decoder allocations, mapped libraries, and kernel memory. The .NET 10 heap tool
uses lossy event buffering; these are captured inventories, not exact limits.

A two-minute sample after poster isolation had median shell PSS of 787.1 MiB;
the preceding intermediate build's one-minute sample had median PSS of 884.8 MiB.
These samples differ in process age, graphics warming, and concurrent workloads.
The original earlier sample had median PSS of 759.2 MiB. Consequently this pass
**does not demonstrate a net reduction in total shell RAM against that original
baseline**, and does not establish a reduction in total OS memory. The software
graphics library remains mapped: poster isolation does not eliminate every user
of that library.

The final fixed-workload benchmark passed its readiness and result correctness
checks, with five measured samples and one warmup. Median application startup was
approximately 2.61 seconds for Files, 2.63 seconds for Notes, and 2.57 seconds for
Settings. The Files fixture contains 1,000 entries; Notes contains 20 fixture
notes. Model enumeration/filter/sort medians were 14.70/1.10/1.83 ms. Launcher
search and show/hide medians were 208.16/138.60 ms in the **headless Skia harness**.
These are not physical-display input latency or frame-pacing measurements.
Several workloads remain above the protocol's reference budgets. No controlled
before/after startup speedup is claimed.

Raw reports and logs are under `artifacts/performance/2026-10-14-memory/`.
The performance score is an internal workload score, not a comparison with
Windows or macOS.

## Verification

- Embedded host, JSON GET/POST, preview cancellation/cleanup, malformed pixel
  output, application activation, and logout: 15 focused tests passed.
- Wallpaper ownership, source-cache leases, hidden console activity, and related
  wallpaper tests: 14 focused tests passed; the earlier broader rendering group
  also passed.
- All twelve video color fixtures pass; the helper's pixels match the existing
  renderer byte for byte, including orientation and wide-gamut conversion.
- Native compositor session checks: zero required failures or warnings.
- LocalSend HTTPS identity endpoint responds; no preview worker remains afterward.
- Live desktop and settled launcher screenshots were inspected. This does not
  establish physical animation pacing, HDR color accuracy, or every deferred
  overlay's first-open interaction matrix.
- Publish contract and source-only icon input checks pass; `git diff --check` passes.

## macOS findings

Apple describes memory pressure in terms of available memory, swap activity,
wired memory, and cached files. macOS caches reusable data and compresses inactive
application memory under pressure. A small application number is not a measure
of the entire desktop's footprint. See [Apple's memory usage guide](https://support.apple.com/en-euro/guide/activity-monitor/actmntr1004/mac)
and [Apple's memory footprint guidance](https://developer.apple.com/library/archive/technotes/tn2434/_index.html).

The relevant principles here are to reduce dirty retained allocations, avoid
unneeded background work, and retain useful reclaimable caches. DaisyOS's image
already configures zstd compressed RAM swap through zram (up to 4 GiB). The
native-development installer now applies the same zram policy and adds a
low-priority 4 GiB disk fallback on supported filesystems. A boot health unit
reports missing zram or systemd-oomd rather than treating configuration as proof
of activation. Measurements made before that installer runs still represent a
no-swap host.

## Remaining performance work

A controlled published-build launch comparison, longer interaction/graphics
soaks, first-open checks for deferred overlays, and high-resolution wallpaper
GPU/decoder profiling are still needed. This pass preserves existing graphics
cache defaults because a smaller-cache experiment did not demonstrate reliable
savings. It does not claim universal smoothness or minimum RAM usage.
