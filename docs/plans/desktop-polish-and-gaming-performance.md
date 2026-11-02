# DaisyOS smoothness and polish plan

Date: 2026-11-02

## Implementation status

The initial implementation and local verification are recorded in
[the implementation report](../validation/desktop-polish-2026-11-02.md).
Render-clock inertia, velocity-aware settlement, launcher allocation reduction,
gaming-aware catalog loading, bounded diagnostics and comparison tools are in
place. Existing visual/layout checks pass with the design language preserved.
Actual presentation/input-latency capture, physical high-refresh/trackpad checks,
longer soaks and controlled gameplay non-regression gates remain outstanding;
the running desktop has not been updated by this implementation pass.

## Objective and scope

Make everyday DaisyOS interactions feel continuous, immediate, and visually
finished while preserving DaisyOS's current design language and gaming performance.
macOS 26/27 are interaction-quality references, not visual templates.

Preserve the existing palette, typography family, icon system, material identity,
control shapes, and overall layouts. Refine alignment, spacing consistency,
legibility, rendering quality, state feedback, and motion within those rules.
No toolkit rewrite, broad restyling, new glass effects, or decorative animation
is part of this plan.

## Rules applying to every phase

- Use Release builds for performance decisions. Keep Debug/headless checks for
  correctness; do not describe them as physical presentation measurements.
- Add no continuously running shell animation loop or permanent high-frequency
  polling. Frame callbacks run only while a visible interaction needs them.
- Keep file I/O, decoding, discovery, and expensive preparation off the UI thread.
- Preserve existing Game Mode/fullscreen prefetch suspension, desktop wallpaper
  pausing, covered-clock suspension, and fullscreen window-effect exclusions.
- Avoid additional fullscreen surfaces, forced composition, graphics flushes,
  CPU-priority changes, or GPU/display tuning as incidental polish changes.
- Preserve supported direct scanout and VRR eligibility. Inspect actual behavior;
  fullscreen alone does not prove direct scanout is active.
- Treat fullscreen, borderless, and windowed gaming as separate cases. Visible
  desktop regions may still render during a windowed game, but use bounded work.
- Hide/close/cancel paths stop work and release temporary resources. No bulk cache
  warming, decoding, or deferred maintenance burst when returning from a game.
- Keep changes small and independently reversible. Each phase must pass its
  desktop and gaming gates before becoming the comparison baseline.

## Phase 1 — Establish trustworthy baselines

Capture the current published/Release build before further implementation. Record
commit, driver, compositor, GPU, output resolution/scale/refresh, VRR configuration,
power mode, wallpaper, overlays, and workload settings.

Desktop scenarios: scroll and reverse in Settings/Files; open, search, page, and
interrupt the launcher; enter/leave overview; open/close shell panels; move,
resize, minimize, and restore windows; switch focus and return from a game.
Test cold and warm interactions separately, at supported 60 Hz and high-refresh
settings, with representative fractional scaling and large content collections.

Measure event handling, preparation, layout/render, and compositor presentation
separately. Record displayed frame intervals, missed refreshes, input-to-first-
visible-response where measurable, UI-thread stalls, allocations/GC, and shell
CPU/GPU/memory use. A Wayland frame callback is a scheduling signal, not proof
of when pixels were presented. Use presentation feedback/compositor tooling where
supported, and an external camera for physical latency checks where necessary.
Make active tracing opt-in and bounded; remove capture instrumentation and its
overlays from final gaming comparisons.

Gaming baseline: repeat identical game scenes or built-in benchmarks in fullscreen,
borderless, and windowed modes. Include GPU-bound and CPU-bound workloads, with
Game Mode on/off, static/live wallpaper, and optional overlay on/off. Use at least
five measured runs per selected case after warmup, alternating baseline/candidate
order. Compare average FPS, slowest-1%-mean FPS, p99 frame time, and stutter counts.
Record scanout/VRR state and shell contention separately from game metrics.

Deliverable: reproducible captures, environment manifest, comparison report, and
an ordered list of measured bottlenecks. Do not infer present-day performance
from older repository reports.

## Phase 2 — Make scrolling and gesture release continuous

Start with the precise-scroll implementation in Avalonia's ScrollContentPresenter.
Move inertia from the 16 ms dispatcher timer to the existing render-frame clock.
Use elapsed time for deceleration so distance and duration do not depend on
refresh rate. Preserve direct finger tracking, final motion in release frames,
horizontal scrolling, logical-content units, snapping, and scroll chaining.

Use recent event timestamps for release velocity. Deliberate reversal and a pause
before release must remove stale momentum. A new gesture takes control immediately.
Stop callbacks on settlement, cancellation, detachment, hidden state, or replacement.
Account for platform-provided momentum to avoid applying inertia twice.

Gate: equivalent motion at different refresh rates; no updates after stopping;
tests for reversal, paused release, edges, nested viewers, logical/virtualized
content, snapping, disabled inertia, and final-motion release frames. Verify the
feel on real trackpads, not just synthetic wheel events.

## Phase 3 — Remove delays before and during motion

Profile launcher preparation, tile binding, layout, icon assignment, search, and
page changes. Existing headless results identify candidates but not frame costs.
Reuse visible controls and prepared data; bound caches and result work; cancel
superseded requests. Avoid full layout or tree rebuilds for per-frame transforms.

Show immediate feedback while expensive content is prepared asynchronously. Keep
geometry stable as icons/results arrive. Prepare only what the visible interaction
requires; do not move latency into uncontrolled background work during games.

Gate: improvement in the measured dominant stages, no correctness or memory-growth
regression, and no delayed work burst during or after gaming. Re-run realistic
large-content and first-open cases.

## Phase 4 — Coordinate motion across shell and compositor

Define a small shared behavioral specification for controls, panels, launcher,
overview, and windows: timing roles, interruption rules, gesture ownership,
settlement, reduced motion, and endpoint cleanup. Reuse existing motion tokens.
Keep Avalonia and KWin implementations where each is appropriate; align their
behavior through configuration/contracts rather than introducing a new engine.

For gesture-driven transitions, preserve position and release velocity when
retargeting. Use a bounded spring or another velocity-aware curve where it helps;
do not apply spring motion to every control. Discrete hover/press feedback remains
short and restrained. Prevent overlapping animations from fighting over transforms
or blocking new input. Avoid duplicating app and compositor entrance effects.

Gate: rapid open/close, reversal, interruption, repeated gestures, focus changes,
and reduced motion all remain predictable. Content and material geometry settle
together without flashes, residual transforms, or permanently softened text.

## Phase 5 — Refine the existing visual language

Audit Files, Settings, launcher, overview, system panels, menus, and dialogs in
light/dark themes and representative display scales. Review screenshots alongside
interaction recordings rather than evaluating settled screenshots alone.

Correct spacing/alignment drift, text baselines, icon optical alignment, clipping,
focus indicators, hover/pressed/disabled state inconsistencies, shadow seams, and
material legibility. Check nested corner relationships using current shape tokens.
Keep loading, empty, error, selection, keyboard, and accessibility states coherent.
Make targeted token/component corrections instead of creating screen-specific styles.

Profile blur-region and material updates before changing their scheduling: geometry
and rendered content must stay synchronized. Do not add heavier effects to mask
interaction problems.

Gate: reviewed before/after captures establish consistency within DaisyOS's current
identity. Changes preserve contrast, hit targets, accessibility, and frame budgets.

## Phase 6 — Enforce desktop and gaming acceptance

Desktop targets are proposed engineering goals, not claims about current results:

- During sustained motion, aim for at least 99% of presented frame intervals to
  stay within 1.5 times the active refresh interval in controlled supported cases.
  Classify intentional idle/adaptive-refresh gaps separately from animation hitches.
- Keep first visible feedback within two refresh intervals for warm, lightweight
  interactions. Track content readiness separately from that acknowledgment.
- No repeatable multi-frame stall, endpoint flash, gesture jump, or post-close
  animation work in the core interaction suite.
- Investigate stages approaching a full refresh interval: 16.7 ms at 60 Hz,
  8.3 ms at 120 Hz, and 5.6 ms at 180 Hz. Individual UI stages need headroom for
  rendering and composition; those intervals are not independent stage allowances.

Gaming gates:

- No repeatable deterioration outside measured baseline variability in average FPS,
  slowest-1%-mean FPS, p99 frame time, or stutter frequency. Target zero regression;
  inconclusive/noisy results require more runs rather than a claimed pass.
- No loss of supported scanout/VRR behavior and no extra hidden-shell render loop,
  wallpaper playback, prefetch, or high-frequency sampling during games.
- No unbounded cache growth or new sustained shell CPU/GPU contention; inspect
  transitions into/out of games and optional overlays separately.
- If a phase fails a gaming gate, optimize or revert that phase before proceeding.

Run a longer session covering display changes, suspend/resume, lock/unlock,
multiple monitors, repeated game launch/exit, and repeated overlay activation.
Publish the measured limits and retain the reproducer for regressions.

## Implementation sequence

1. Baselines and measurement capability.
2. Render-clock scrolling and gesture correctness.
3. Measured launcher/UI bottlenecks.
4. Shared motion behavior and velocity-aware settlement.
5. Visual consistency fixes using existing design tokens.
6. Final desktop/gaming comparison and regression gates.

Investigate any severe measured bottleneck earlier, but preserve the baseline and
gaming gate for each change. No fixed completion estimate is credible until the
first phase establishes the hardware-specific bottlenecks.

## Evidence and references

- [Launcher measurements](../validation/launcher-performance-2026-10-31.md):
  headless workload timings, explicitly excluding physical presentation.
- [Existing gaming safeguards](../validation/gaming-performance-2026-10-15.md):
  background contention changes; no controlled gameplay improvement established.
- [Performance and memory verification](../architecture/performance-and-memory.md).
- [Apple: Understanding user interface responsiveness](https://developer.apple.com/documentation/xcode/understanding-user-interface-responsiveness).
- [Apple: Understanding hitches](https://developer.apple.com/documentation/xcode/understanding-hitches-in-your-app).
- [Apple: Designing Fluid Interfaces](https://developer.apple.com/videos/play/wwdc2018/803/).
- [Apple: AppKit and the macOS 26 design](https://developer.apple.com/videos/play/wwdc2025/310/).
- [Apple: macOS 27](https://www.apple.com/os/macos/).
