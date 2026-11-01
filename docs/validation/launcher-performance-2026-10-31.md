# Launcher responsiveness — 2026-10-31

The launcher now recycles complete tile controls when changing search results
or pages. The fixed-cell grid retains its containers, and `LauncherItemsControl`
keeps the template-built button content intact while changing its data context.
Search labels, selection, icons, and click targets bind to the current item.
Live calendar controls are created only for actual calendar tiles, rather than
for every application in the grid.

Page snapshots are cached until filtering or page dimensions change. Reopening
resets the page before revealing the launcher and suppresses the separate page
transition. An already-resolved queued search does not run again and reset the
page after navigation.

Background icon preloading runs in batches of four. Visible tiles can register
immediately without waiting behind an entire catalog of semaphore waiters.
Worker continuations stay off the UI dispatcher; bitmap assignment returns to
the UI thread. Superseded or cancelled batches cannot restore stale icons.

## Measurement

The deterministic responsiveness harness uses 2,000 synthetic applications,
1,280 × 900 output, five discarded warmups, and 40 measured samples. Both runs
used Debug builds, the same queries and paging inputs, and headless Skia render
ticks. Intentional animation durations are disabled to measure preparation and
render work. Synthetic icons are empty; these numbers do not measure icon
file decoding, KWin presentation, physical input latency, or animation FPS.

| Operation | Before median / p95 | After median / p95 | Median reduction | UI-thread allocation reduction |
| --- | ---: | ---: | ---: | ---: |
| Search and render | 157.3 / 168.3 ms | 87.3 / 99.7 ms | 44% | 75% |
| Page change and render | 229.0 / 242.1 ms | 53.3 / 75.2 ms | 77% | 95% |
| Three swipe movements, release, and render ticks | 505.3 / 1,025.4 ms | 174.6 / 199.6 ms | 65% | 94% |
| Warm opening and render | 236.9 / 252.3 ms | 65.8 / 88.0 ms | 72% | 94% |

Cold construction and first opening were 1,867.5 ms before and 1,791.7 ms after.
Those are single samples, including initialization of the synthetic catalog;
they do not establish a reliable cold-start improvement. Managed heap snapshots
were 30.2 MB before and 32.6 MB after; recycling retains bounded spare tiles.

Raw local reports are `artifacts/launcher-performance/baseline.json` and
`artifacts/launcher-performance/after.json`. Reproduce a run after building the
shell and headless dependencies in Debug:

```sh
dotnet run --project scripts/responsiveness-benchmark/ResponsivenessBenchmark.csproj \
  -- artifacts/launcher-performance/run.json 40
```

The harness uses an isolated state file and an injected catalog, with no real
application discovery or launches. Its protocol 2 report includes sample counts,
medians, p95, worst-case latency, UI-thread allocations, and visible tile count.
The existing OS benchmark query expectation was corrected to 12 matches: token
matching also includes `Application 0012` and `Application 1012`, in addition to
the ten name-prefix matches. Search behavior and that workload are unchanged.

## Regression checks

Launcher paging, tile reuse/bindings, search ranking, keyboard selection,
large-catalog queued search resolution, calendar realization, reveal motion,
trackpad scrolling, and icon preload/replacement/cancellation are covered by
`DaisyOS.ShellDemo.Tests`. All 44 focused checks passed:

```sh
dotnet test tests/DaisyOS.ShellDemo.Tests/DaisyOS.ShellDemo.Tests.csproj --no-restore \
  --filter 'FullyQualifiedName~LauncherPagingTests|FullyQualifiedName~LauncherIconLoaderTests|FullyQualifiedName~GuiMotionTests|FullyQualifiedName~PerformanceBenchmarkIsolationTests|FullyQualifiedName~TrackpadScrollingTests'
```

## Running session

The Release shell and companion applications built successfully, and
`scripts/daisyos-native-development restart` applied the update. The workflow
used its bounded termination fallback. Shell PID changed from 194214 to 216620;
KWin remained PID 36010. The compositor overview remained loaded as v16.
The strict compositor session check reported zero required failures and zero
warnings. No physical launcher frame pacing or hardware swipe timing was measured.
