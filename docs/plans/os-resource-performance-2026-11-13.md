# OS resource and gaming performance targets

These are acceptance targets, not achieved results. Release builds on the same
machine, driver, power profile, display and workload must be compared. Keep raw
reports. A busy developer session is useful for diagnostics but cannot certify
fresh-login idle memory or game performance.

| Area | Testable target | Before/after test |
| --- | --- | --- |
| Desktop idle footprint | At most 700 MiB summed desktop-session PSS after five minutes; reduce baseline by 10% when over budget | Three fresh logins per build using `benchmark-desktop-session.py` and `compare-desktop-session-runs.py` |
| Idle CPU | Shell plus DaisyOS services at most 1% of one CPU averaged over five settled minutes | Same session captures; record inaccessible processes and foreground load |
| App readiness | Settings/Files median ≤750 ms, Notes ≤500 ms; no p95 regression exceeding 5% beyond observed run variation | `benchmark-os-performance.py`, 3 warmups and 30 samples for acceptance |
| Launcher | Search median ≤16 ms and hide/show ≤32 ms in the fixed 2,000-app workload | Same OS runner; headless render cost is separate from presentation |
| Game contention | Zero new explicit trimmer collections, cache reclamation or malloc trimming when Gaming Mode is already active | Linked production-service maintenance benchmark and integration tests |
| Game performance | Target ≥5% improvement in p99 frame time or slowest-1%-mean FPS in a confirmed CPU-bound case; no repeatable >2% regression in any selected case | At least five alternating baseline/candidate runs of the same scene after warmup, using frame logs and `compare-game-frametimes.py` |
| Gaming memory | No swapping or sustained memory PSI in selected gameplay; record shell/service PSS alongside frames | `capture-gaming-kernel-run.py`, desktop sampler and game logs |
| Presentation | Preserve scanout and VRR eligibility; verify fullscreen, borderless and windowed cases separately | Record compositor/driver state with each game capture |

For gaming, hold resolution, quality, frame cap, refresh, VRR, overlays and
wallpaper constant. Compare static/live wallpaper and overlay separately. An
inconclusive result needs further runs. Do not turn a microbenchmark gain into an
FPS claim. Do not replace a service with Rust without measuring its resource
cost: launch and gaming services already use Rust.

## Maintenance regression reproducer

Before modifying the service:

```bash
dotnet run --project scripts/memory-maintenance-benchmark -c Release > /tmp/maintenance-before.json
```

After modifying the service:

```bash
dotnet run --project scripts/memory-maintenance-benchmark -c Release -- --verify > /tmp/maintenance-after.json
```

The harness links the actual maintenance source, uses 100,000 live byte arrays,
two warmups and 15 measured samples, invokes the timer's maintenance body, and
reports wall time, Gen2 collections and hidden-view reclaim calls. Cache stubs
isolate the policy and real GC/finalizer/malloc work from UI dependencies. This
is a maintenance reproducer, not an end-to-end game or memory-footprint test.
The verification requires zero maintenance collections/reclaims during gaming
and after return to visibility, while lock-screen reclamation remains enabled.

Gameplay validation is pending: the user confirms this laptop has no installed games.
See [the measured first-pass results](../validation/os-resource-performance-2026-11-13.md) for achieved policy checks and outstanding targets.

## Application resource targets

For the Notes startup pass, require no rich editor construction until a rich
note is selected, no code editor construction until a text/code document is
selected, and no syntax registry for plain text or an unused editor. Preserve
formatting, typing, undo, document switching and attachment lifecycle behavior.
Target at least a 10% reduction from the newly captured 186.1 MiB Notes PSS
baseline at initial render in the 20-text-note fixture. This target was set
before measuring lazy editor construction. Initial-render PSS is separate from
settled desktop PSS and peak startup memory.

Use `benchmark-app-startup-resources.py` with saved baseline and candidate Shell
assemblies. It alternates paired launches, discards two warmups per variant,
keeps dependencies/fixture/display/appearance identical and records PSS at the
authenticated ready signal. Do not interpret a current-thread allocation
reduction as the same percentage reduction in resident memory.

The [application pass results](../validation/application-resource-performance-2026-11-13.md) retain the startup and 10% memory targets as unmet, with measured gains and the pending administrator/Release-session step recorded.
