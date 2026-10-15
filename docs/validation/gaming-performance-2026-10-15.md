# Gaming background contention — 2026-10-15

This pass targets avoidable shell work during games. It does not establish a before/after game frame-time improvement.

## Changes

- NVIDIA overlay sampling uses the optional NVML library directly, keeps initialization between samples, balances shutdown, and retries missing/lost devices after thirty seconds. Sampling and `/proc` metrics reads run off the UI thread. There is no per-second `nvidia-smi` process.
- App prefetch stops during Gaming Mode or any fullscreen application, including new observations/cache saves. Exiting returns it to the existing four-second idle delay. Synchronous Linux readahead executes on a worker without holding the cancellation/input lock. Cancellation sources remain owned by their individual workers until completion. Already-submitted kernel I/O cannot be canceled by this policy.
- Desktop live wallpaper playback and associated preparation demand pause during Gaming Mode even for windowed games. Lock-screen surfaces ignore this desktop-only policy. Normal playback demand resumes when Gaming Mode ends.
- Animated desktop clock render timers stop while the desktop is covered or Gaming Mode is active; they update to current time and resume on return. Previously these clocks issued roughly sixty render updates per second even while covered.
- A frame-log analyzer reports average FPS, slowest-1%-mean FPS, p99/max frame times, and stutter counts. Equal scene/settings and per-frame logs are required for comparisons.

## Local telemetry comparison

Twenty-five sequential samples per method, on the live NVIDIA GPU, outside gameplay:

| Method | Median query wall time | Maximum query wall time |
| --- | ---: | ---: |
| `nvidia-smi` subprocess | 22.96 ms | 45.58 ms |
| Initialized direct NVML query | 1.15 ms | 15.58 ms |

This is a Python/ctypes API-path comparison, not a measurement of C# allocation costs or game FPS. The direct calls still have driver cost; eliminating process startup is not proof of a particular game frame-time gain.

## Current diagnostic boundary

The installed GameMode helper access check now passes for the desktop user. A read-only snapshot outside a game showed the CPU governor at `schedutil`; this is not a failure by itself, since GameMode should restore the governor when requests end. Recent I/O PSI was nonzero (one snapshot: `some avg10=3.56`, `full avg10=3.36`), memory PSI was zero, and `/sys/block/sdb/queue/rotational` was `1`. These observations are not from a controlled gameplay run and cannot identify the cause of the user's frame spikes.

The existing Git object corruption prevented `git status` at the start of this pass. No repository metadata was repaired or removed. Working-tree whitespace validation remains available.

The active output is DP-3 at 2560×1440, 179.952 Hz; KWin reports Adaptive Sync `never`. At a synchronized fixed refresh rate, 120 FPS does not divide evenly into approximately 180 Hz. This is a plausible presentation-cadence contributor, not a measured explanation of the reported microstutter. Compare supported adaptive sync or a matched refresh/frame-rate combination separately from loading stalls. Display settings were not changed in this pass.

GameMode’s built-in `gamemoded -t` completed with all tests passing, including temporary governor switching and I/O priority. It reported GPU tuning and renice as not configured, so those were not validated as active optimizations. Raw telemetry-comparison and GameMode logs are in `artifacts/gaming-performance-2026-10-15/`.
