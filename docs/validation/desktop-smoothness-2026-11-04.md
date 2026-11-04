# Desktop smoothness — 2026-11-04

## Research and constraints

Apple's [UI responsiveness guidance](https://developer.apple.com/documentation/xcode/understanding-user-interface-responsiveness)
distinguishes interaction latency from hitches: fluid motion requires meeting the
display's refresh deadlines. Its [macOS 26 AppKit design session](https://developer.apple.com/videos/play/wwdc2025/310/)
describes consistent controls and a hierarchy separating controls from content.
The [official macOS 27 page](https://www.apple.com/os/macos/) describes readability,
contrast, consistency and responsiveness refinements. These are documented design
goals, not independent measurements proving either OS always runs smoothly.
No macOS hardware comparison was performed. Apple implementation details and
animation constants are not inferred from marketing material.

DaisyOS already implements interruptible motion, bounded asynchronous icons,
virtualized Files views, render-driven scroll inertia, and compositor window
transitions. This pass keeps those systems and the existing appearance. It does
not add glass effects or longer transitions as a substitute for responsiveness.

## Defects and changes

- The native acceptance trace exposed excessive animation callbacks while Wayland
  surfaces waited for frame permission. Surface readiness now advertises that a
  frame callback will wake rendering. The Wayland worker defers animation-only
  batches when every target with pending rendering is waiting on the platform.
  Idle windows do not prevent deferral. Platform/configure wakeups, queued native
  lifecycle jobs, and the existing 20 Hz starvation fallback bypass the gate.
  No fixed 60 Hz animation timer or display mode change is introduced.
- Shared slide/scale reversals previously retained velocity but restarted the full
  duration. They now scale duration by remaining translation/scale/opacity range,
  bounded by the original duration and a 40 ms minimum for nonzero travel. A
  reversal already at its destination settles immediately.
- Activity gates previously restarted consumer work for every positive opacity
  change. They now publish only changes between active and inactive, and detach
  removes ancestor subscriptions.
- Theme transition property discovery is cached per control type instead of
  repeatedly scanning property registrations. Existing transition ownership,
  disabled transitions, focus/hover feedback, and popup transparency are preserved.
- Runtime-selected notification glyphs are explicitly packaged; the quick-settings
  slider default uses the existing speaker icon alias. This fixes missing icons
  and avoids failed resource loads without changing their intended appearance.
- Notes pinning/filter changes now remove, move or insert affected rows rather
  than resetting the collection. Surviving rows keep their identity. The file
  picker reads/decodes external notes off the UI thread with two bounded readers;
  cancellation and selection/request identity reject obsolete completions.
  The existing synchronous API remains compatible. Autosave/storage semantics
  are outside this change.

## Diagnostics and reproduction

Flyout, Settings-navigation and Notes-selection handlers emit opt-in
`feedback.callback` measurements. These end at a UI callback, not compositor
presentation. They contain fixed operation names, never note text or file paths.
The headless launcher harness additionally records raw timing samples and median
absolute deviations. Cold construction remains one process-local sample; warm
opening is sampled repeatedly. Existing application-launch benchmarks retain
fresh-process versus warmed-cache boundary labels; no claim of disk-cold launch
performance is made.

The trace reporter retains its original stdout interface. With `--output` it
adds per-operation summaries, GC counters with separate units, output refresh
rate/scaling, GPU details and active compositor support information. Metadata is
collected at reporting time, so generate it immediately after capture without
changing the display configuration. Mixed-refresh captures require an explicit
rate to calculate callback budgets. At 60.02 Hz the budget is about 16.66 ms;
120 Hz corresponds to 8.33 ms. Neither budget converts callbacks into presented FPS.

```sh
dotnet build src/DaisyOS.Shell/DaisyOS.Shell.csproj -c Release --no-restore -m:2
dotnet run --project scripts/responsiveness-benchmark -c Release -- artifacts/smoothness/launcher.json 20
bash scripts/run-smoothness-acceptance.sh artifacts/smoothness/native
bash scripts/run-smoothness-acceptance.sh --virtual artifacts/smoothness/virtual
python3 scripts/report-responsiveness.py artifacts/smoothness/native/callbacks.jsonl --output artifacts/smoothness/native/report.json
python3 scripts/test-polish-reports.py
python3 scripts/test-responsiveness-report.py
python3 scripts/test-kwin-window-motion-runtime.py
bash scripts/check-compositor-session.sh --strict
```

The native harness loads the current Release shell controls in a disposable
Wayland window and isolates configuration in a temporary directory. It exercises
scripted actions, not physical input. It neither replaces the installed shell nor
establishes layer-shell blur correctness. Rebuild the shell before running the
harness; its assembly references deliberately use the built Release output.

## Acceptance limits

The available output is 1920×1080 at approximately 60.02 Hz, 125% scaling. No
120 Hz output or multiple monitors are available. Passing callback/lifecycle
checks cannot certify physical touchpad feel, presentation pacing, blur/input
alignment in the installed shell, or all GPU configurations. The native test window received almost no compositor frame callbacks during this
session, so its callback timings cannot validate active display pacing. An isolated
virtual KWin session is used separately to test active frame permission and
backpressure. The installed shell is not restarted by the acceptance harness. Those checks remain explicitly open.

## Results

The controlled virtual-KWin comparison used the same current shell and acceptance
harness, swapping only `Avalonia.Base.dll` and `Avalonia.Wayland.dll` for the
preserved pre-pacing binaries. The workload exercises all flyouts, repeated
launcher reveals, toast replacement/detachment, Files list/grid virtualization,
Settings navigation, Notes selection, rapid scale reversals and cancellation.

| Virtual compositor run | Animation interval samples | Platform frame callbacks | Outcome |
| --- | ---: | ---: | --- |
| Original toolkit | 60,708 | At least 10; exact count not persisted on failure | Fails bounded-callback regression gate |
| Patched toolkit | 537 | 361 | Passes all eight acceptance groups |
| Patched repeat with isolated KWin configuration | 546 | 366 | Passes all eight acceptance groups |

The comparison removes about 99.1% of redundant animation interval samples in
this scripted workload. Multiple controls and callbacks can share a frame; these
counts are not display FPS. Patched callback p95 was 15.97 ms, and 15.57 ms in the
repeat; worst intervals were 65.22 and 91.49 ms. Initial construction and context
switching still produce outliers. No hitch-free or physical-latency certification
is claimed. The virtual output supplies frame permission without depending on
the visibility of a window in the physical desktop session.

The original 2,000-app, 20-sample headless launcher baseline measured search,
page, swipe sequence, and warm-open medians of 37.95, 30.00, 86.57, and 39.04 ms.
The first interaction-only candidate measured 38.19, 30.18, 90.64, and 39.29 ms.
Search allocations fell from 726,816 to 688,688 median UI-thread bytes. A later
full-build run overlapped other validation and was slower; these are not
controlled latency-equivalence results. Cold construction is a single sample
and is not evidence of a startup speedup. Reports now retain raw samples and MAD
for future repeatability comparisons. An additional quiet final run measured medians of 37.85, 30.13, 97.06, 41.94 ms,
with search p95 49.55 ms and 684,544 median allocated bytes.
This supports similar headless preparation cost with reduced search allocations,
but does not establish physical latency equivalence. The OS benchmark additionally retains the
first launch per app separately from warmed runs, explicitly marking uncontrolled
cache state and whether the first launch was included in the summary.

Verification completed:

- Release shell and native acceptance harness build successfully.
- 72 focused motion/launcher/Notes/regression tests pass; 8 scrolling tests pass;
  24 notification, Settings layout, theme and material-frame tests pass.
- Both trace-report suites pass (6 tests), and OS benchmark/report checks pass
  (12 tests). The existing icon-bundle fixture test fails on its unchanged
  weight expectations (`play-fill.svg`/`pause-fill.svg` versus unweighted names).
  Actual packaged runtime glyph availability passes the new UI regression test.
- KWin JavaScript motion tests and all 16 live transition/settlement checks pass.
  The strict live compositor session report has zero failures and zero warnings.
- A combined broad UI run emitted an Avalonia thread-affinity cleanup failure.
  Rerunning the relevant suites separately produced the passing counts above;
  the combined run is not treated as a clean pass.

Raw local evidence is under `artifacts/smoothness-2026-11-04/`, including
`before.json`, `after.json`, `final-launcher.json`, `quiet-launcher.json`, `virtual-before/`,
`virtual-after/`, and `virtual-final/`. Build artifacts and captures are not
committed. The baseline toolkit deliberately fails the new regression gate;
its `failure.txt` is expected evidence, not a candidate failure.
