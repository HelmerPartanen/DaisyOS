# Desktop polish implementation — 2026-11-02

## Delivered

The current DaisyOS design language and theme resources are unchanged. This pass
changes motion, repeated UI work, gaming background policy, and measurement tools.

- Precise-scroll inertia now follows render callbacks instead of a 16 ms dispatcher
  timer. Analytic exponential integration gives equivalent travel at different
  refresh rates and includes delayed frames. Stop thresholds use physical pixels
  even when a logical panel expresses its offset in items.
- Each fling has a generation-protected callback chain. New input, hidden ancestors,
  detachment, replacement, disabled inertia, and settlement stop the chain. There
  is no continuous idle scrolling timer. Synthetic momentum starts only on the
  explicit precise-scroll release contract, not arbitrary native wheel streams.
- Launcher paging and pinch release use finite velocity-aware Hermite settlement.
  Source-side pinch velocity survives D-Bus coalescing; older payloads remain valid.
  KWin overview uses the same settlement equation and correctly changes the sign
  for closing gestures. Normal discrete overview transitions retain cubic-out motion.
- Interrupted interior slide/scale animations retain their measured velocity as
  well as position. Hidden/detached content stops requesting animation frames.
- Recycled launcher tiles retain their data context until rebinding. This avoids
  a redundant null-binding pass on every page/search replacement. The existing
  fixed-cell panel hides pooled tiles and bounds the spare pool at 128 per key.
- Launcher catalog icon preloading now suspends during Game Mode/fullscreen use.
  Outstanding off-screen requests cancel, visible tiles remain demand-loadable,
  and leaving a game does not automatically restart a catalog-loading burst.
- Existing wallpaper/clock/prefetch suspension and fullscreen window exclusions
  remain in place. No new fullscreen surface, compositor effect, permanent render
  loop, display tuning, or scheduling-priority change was introduced.

## Measurement tools

The responsiveness harness incorrectly referenced Debug shell/headless assemblies
even when invoked with `-c Release`. References now follow the build configuration.
Reports include the shell assembly configuration/hash and explicitly state that
presentation was not measured.

`scripts/capture-polish-environment.py` captures bounded, read-only driver,
compositor, output, power-profile, runtime, and revision information. Unavailable
commands are recorded as unavailable, not silently treated as successful checks.

Interior animations emit callback intervals only with opt-in responsiveness tracing.
Tracing expires after 30 seconds by default (configurable from 1–600 seconds) and
has an event limit. `report-responsiveness.py --refresh-hz` reports callback cadence
separately from other timings and never labels it compositor presentation.

`scripts/compare-game-frametimes.py` compares at least five distinct capture files
per build, reports average FPS, slowest-1%-mean FPS, p99 frame time and stutter
fractions, and rejects highly variable baselines as inconclusive. It is a descriptive
repeatability check, not statistical equivalence certification. Its report leaves
`gamingGatePassed` false because gameplay logs alone cannot establish scanout/VRR,
environment equivalence, or absence of shell contention.

Example commands:

```sh
python3 scripts/capture-polish-environment.py artifacts/polish/environment.json --label candidate
dotnet build src/DaisyOS.Shell/DaisyOS.Shell.csproj -c Release --no-restore
dotnet run -c Release --project scripts/responsiveness-benchmark/ResponsivenessBenchmark.csproj -- artifacts/polish/launcher.json 40
python3 scripts/report-responsiveness.py /path/to/trace.jsonl --refresh-hz 120
python3 scripts/compare-game-frametimes.py --baseline /path/to/baseline-*.csv --candidate /path/to/candidate-*.csv
```

For tracing, start an isolated acceptance shell with
`DAISYOS_RESPONSIVENESS_TRACE=/path/to/trace.jsonl` and optionally
`DAISYOS_RESPONSIVENESS_SECONDS=60`. Do not enable tracing or capture overlays in
final gameplay comparisons. Game logs must contain per-frame samples, such as
MangoHud `log_interval=0`; use identical scenes/settings and alternate run order.

## Local Release workload results

The fixture uses 2,000 synthetic apps, a 1280×900 headless Skia window, and five
discarded warmups. Intentional animation durations are disabled and fixture icons
are empty. The baseline has 20 measured samples and the final candidate has 40.

| Operation | Baseline median / p95 ms | Candidate median / p95 ms | Median UI-thread allocated bytes, before → after |
| --- | ---: | ---: | ---: |
| Search and render | 61.86 / 75.42 | 61.75 / 77.25 | 2,316,248 → 1,962,576 |
| Page change and render | 43.00 / 68.48 | 37.44 / 52.21 | 806,016 → 403,928 |
| Three swipe movements, release, and render ticks | 140.67 / 159.48 | 130.77 / 152.39 | 1,756,096 → 765,536 |
| Warm opening and render | 57.72 / 74.02 | 51.99 / 62.13 | 936,216 → 533,144 |

Allocation reductions are approximately 15%, 50%, 56%, and 43%, respectively.
Additional candidate runs showed timing variation; no statistically controlled
speedup, physical latency improvement, display FPS, or gaming improvement is claimed.
The current fixture still contains substantial render work. End-of-run estimated
managed heap was 27.2 MB before and 45.6 MB after; these uncollected snapshots differ
in workload length and collection history and do not establish a retained-memory
increase or reduction. A comparable memory/graphics soak remains necessary.

Raw environment and workload reports are in `artifacts/polish-2026-11-02/`:
`environment.json`, `baseline.json`, `candidate.json`, `candidate-quiet.json`, and
`candidate-final.json`. The final environment-independent build identity is in the
candidate reports. The captured live host has a 1920×1080 approximately 60 Hz output;
the synthetic 120/180 Hz tests are mathematical/correctness checks, not physical
high-refresh validation.

## Verification

- Release shell build passes.
- Focused shell interactions: 56 tests pass, covering scrolling, refresh-independent
  travel, reversal/edges, hidden-state cancellation, finite velocity settlement,
  pinch compatibility, launcher paging/icon cancellation, interior motion, and
  gaming clock/automation behavior.
- Gaming policy, telemetry, overlay, wallpaper optimization, swipe physics and pinch
  dispatch: 40 tests pass.
- Existing Files, controls, dialogs and Settings polish/layout suites: 26 tests
  pass. Inspected Files captures in dark mode and light mode with 150% typography,
  plus the full-output launcher capture. No theme/style changes were made from
  these checks. Headless captures cannot validate live compositor materials.
- Production QML gesture/settlement suites: 46 checks pass, including the production
  overview state machine with a fake scene delegate. These do not exercise live
  KWin thumbnails or physical touchpad recognition.
- Frame-log/report unit tests: 9 pass. Existing automatic/user gaming-integration
  checks: 7 pass.
- The delayed-first-frame test now samples the first actual render callback, drains
  the headless composition transaction, and allows a delayed later observation to
  have reached its endpoint. It still verifies that pre-first-frame mapping delay
  consumes no entrance motion.

## Acceptance still outstanding

This is an implementation and local verification pass, not completion of every
hardware acceptance gate in the plan. The running desktop/compositor was not
restarted or updated during this pass.

Remaining: actual compositor presentation/input-latency captures, real trackpad
feel, supported high-refresh/fractional-scale/multi-monitor coverage, a longer
memory/graphics soak, live material/transition recordings, and repeated gameplay
captures for fullscreen/borderless/windowed and overlay cases with scanout/VRR
inspection. No game was launched or settings changed for synthetic validation.
Do not call gaming non-regression certified until those measurements pass.
