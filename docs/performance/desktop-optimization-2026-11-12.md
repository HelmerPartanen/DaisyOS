# Desktop optimization and console removal

The shell now has one desktop experience. Console home, settings, media,
quick-menu and transition views, the console native surface, controller shell
navigation/keybinding code, and unused console strings have been removed.
The two-second controller probe no longer runs. Gaming Mode's performance toggle
and game discovery/artwork services remain available independently.

Application catalog readers share a parsed, read-only list in `DaisyLaunchClient`.
Concurrent cache misses issue one request. Registry/launcher-owner changes and
UI-language changes invalidate the snapshot. A change during an outstanding
request prevents that response from satisfying the next cache lookup. Failed
requests are not cached, and cancelling a queued reader leaves other readers
unaffected.

The notification settings page displays its loading state immediately and
performs catalog discovery and icon-path resolution outside the UI thread.
A category generation check prevents a late response from adding rows to a
subsequently selected page.

The existing Rust launch broker now waits for a returned worker or its idle
lease deadline instead of checking the pool every 100 milliseconds. The
existing two-second lease, helper reuse, cancellation and retirement behavior
are preserved.

## Measurement

An isolated private D-Bus session with a mock empty catalog and no launches
compared Debug binaries using the original and updated `worker.rs`. Over one
three-second sample per binary, voluntary context switches summed across broker
threads fell from 60 to 0. This measures the removal of idle polling, not CPU
utilization, application readiness, or whole-desktop memory savings. Raw samples,
baseline commit and binary hashes are in
[desktop-idle-2026-11-12.json](desktop-idle-2026-11-12.json).

The existing 2,000-application headless responsiveness workload also completed.
It uses Debug assemblies and CPU-rendered Skia ticks; it does not measure real
Wayland presentation and was not a controlled before/after comparison. No
claim that all applications open instantly follows from these checks.

## Validation and limits

- Shell and UI test projects build with zero errors. Existing analyzer warnings remain.
- 47 Quick Settings, motion, media and Settings layout tests pass.
- 29 launcher panel/paging tests pass.
- Four catalog cache tests and three controller discovery tests pass.
- The private-bus broker suite covers validation, idempotence, malformed/crashed/
  missing helpers, cancellation, limits, timeout, worker reuse, lease renewal,
  expiry and shutdown.
- Rust Clippy passes with warnings denied.

No additional service was rewritten in Rust during this change. Launch and
idle-monitor code already use Rust. The managed portal and Continuity services
remain larger migration candidates described in
[background-rust-migration.md](background-rust-migration.md); replacing them
requires preserving their protocols and on-demand UI behavior, followed by
separate memory and startup comparisons.

These are repository changes. Builds and private-bus tests do not replace or
restart the installed desktop or its services.
