# Resource and gaming contention pass — 2026-11-13

The first measured change removes explicit shell heap maintenance while Gaming
Mode is active and discards delayed aggressive requests if the shell has become
visible again. Lock/display-off maintenance remains available outside Gaming
Mode. Runtime GC and the operating system's memory-pressure policy remain active.
Maintenance already in progress when gaming begins cannot be canceled.

Targets and acceptance commands are in
[the performance target plan](../plans/os-resource-performance-2026-11-13.md).
The gaming and launch daemons already use Rust; this pass did not rewrite them.

## Isolated before/after measurement

Release, .NET SDK 10.0.114, Linux x64. Production service source linked into
`scripts/memory-maintenance-benchmark`; cache dependencies stubbed. Each sample
holds 100,000 arrays of 128–255 bytes live. Two warmups, 15 measured samples.
The developer desktop remained open, so timing variation is expected.

| Timer-body scenario | Before median / p95 | After median / p95 | Before / after Gen2 collections, summed over 15 samples |
| --- | ---: | ---: | ---: |
| Hidden shell during gaming | 26.015 / 28.613 ms | 0.013 / 0.034 ms | 15 / 0 |
| Returned to desktop before delayed request fires | 25.813 / 27.596 ms | 0.017 / 0.036 ms | 15 / 0 |
| Locked outside gaming | 24.982 / 25.696 ms | 27.236 / 34.272 ms | 15 / 15 |
| Non-aggressive maintenance during gaming | 0.292 / 2.084 ms | 0.009 / 0.014 ms | 13 / 0 |

The after run passed `--verify`: gaming and returned-visible scenarios made no
cache-reclaim calls or Gen2 collections; all 15 lock-screen scenarios still
reclaimed caches. Lock timing variation is not an improvement claim. The
non-aggressive path requests asynchronous collection, so its elapsed time is
not the total collection cost.

Raw reports and build logs are retained locally under
`artifacts/performance-2026-11-13/`, including source hashes and environment
limitations in `maintenance-environment.json`. This directory is ignored by Git.

## Limits and memory tradeoff

This is removal of a measured maintenance cost, not a measured game frame-time
improvement. The user confirms no games are installed on this laptop. Actual
FPS, slowest-1%-mean FPS, p99 frames, scanout and VRR acceptance remain pending on
a machine with a repeatable game workload.

Unused shell allocations may stay resident longer during Gaming Mode. This
change does not claim lower PSS. Validate the memory/PSI target with gameplay
before accepting the complete gaming gate. Normal GC can still collect during
gaming. No game-exit catch-up maintenance burst was introduced.

The installed running shell has not been replaced or restarted by this pass.

## Full OS workload comparison

The existing fixed-protocol Release runner completed before and after (two
warmups, seven measured runs, Chrome explicitly excluded). The comparator
accepted the recorded environment/protocol pairing. Scores were **691.8 →
693.2 (+0.2%)**, which does not establish a general OS speed improvement.
App medians remained about 2.2–2.5 seconds, above the proposed readiness budgets;
launcher search remained about 30 ms. These are unresolved performance targets.

Individual results varied: Files sort p95 increased from 3.61 to 9.19 ms while
its median changed from 3.33 to 3.37 ms; enumeration median increased while p95
decreased. The desktop was in active development use. Seven samples and this
variation cannot certify the end-to-end regression gate. The trimmer is not
started by these standalone application workloads, so these results must not be
attributed to the new guard. Retain this diagnostic baseline and use 30 samples
in a controlled session for readiness acceptance.

The raw eight-workload comparison is in
`artifacts/performance-2026-11-13/os-comparison.txt` alongside both JSON reports
and per-process logs. Missing game and controlled fresh-login captures remain
explicitly pending; no complete OS/gaming acceptance pass is claimed.

## Verification

- Production-source maintenance harness: all four policy scenarios pass.
- Release `PowerIdleServiceTests`: 16 passed.
- OS benchmark protocol tests: 12 passed.
- Frame-time analyzer tests: 6 passed.
- Desktop-session sampler tests: 3 passed.
- Both full Release OS benchmark runs completed all eight workloads.
- Working-tree whitespace validation passed.

Settings now describes hidden-desktop reclamation and explains that Gaming Mode
pauses cleanup. The persisted preference key is retained for compatibility.

The later [application resource pass](application-resource-performance-2026-11-13.md)
adds typography allocation reductions, lazy Notes editors and syntax highlighting,
paired startup/PSS tests, and reports the installed Debug-session limitation.
