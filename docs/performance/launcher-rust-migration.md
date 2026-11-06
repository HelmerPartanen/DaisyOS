# Resident launcher migration to Rust

The launcher now uses a resident Rust D-Bus broker and an on-demand KDE helper.
This removes Qt/KDE from the idle broker without replacing KService's catalog,
desktop-entry parsing, application actions, KIO dispatch, XWayland discovery,
cursor preferences or scaling behavior with a handwritten approximation.

The repository's default build, local installer, shell publisher and ISO checks
now include both executables. Building and testing did not replace or restart
the user's installed launcher. Desktop services and Continuity remain managed
services; they are not migrated by this change.

## Measurements

Measured on 2026-11-06, Linux x86-64, rustc 1.99.1 (58cf8f9c3 2026-10-13), Qt 6.12.0.
Raw samples and executable hashes: [launcher-rust-wayland-memory.json](launcher-rust-wayland-memory.json).

| Metric | Resident Qt/KDE | Rust broker + on-demand helper |
| --- | ---: | ---: |
| Warm idle PSS (median of three runs) | 13,343 KiB | 1,606 KiB |
| Warm idle RSS | 47,508 KiB | 3,504 KiB |
| Sampled peak process-tree PSS during dispatch | 13,443 KiB | 15,964 KiB |
| Dispatch median | 24.4 ms | 64.7 ms |
| Dispatch p95 | 25.3 ms | 71.2 ms |
| Service startup median | 60.0 ms | 81.2 ms |

**Idle attributable saving: 11,737 KiB (11.46 MiB, 88.0%).**
The cost was about 40.2 ms extra median dispatch time and
2.46 MiB more sampled peak PSS during a launch.
The Rust broker had 2 resident thread and no helper children after the workload.

These measurements are from private session buses with disposable XDG profiles
and real Wayland connections. Qt's platform theme was disabled consistently to
avoid activating unrelated portal/UI daemons in the test bus. The fixture is a
windowless `/bin/true` desktop entry dispatched through real KIO. Each of three
alternating-order runs used three warmups and 30 measured launches. Idle memory
was sampled after the workload, with request records still retained, and no
helper children remaining. The raw report records binary hashes and toolchain
versions. It is not a measurement of whole-system memory or a clean OS boot.

PSS is attributable physical memory; RSS counts shared library pages in full.
Do not multiply RSS reductions or treat virtual-address reservations as RAM
saved. The peak is the maximum broker-plus-descendant PSS observed by a 10 ms
sampler, not a guaranteed instantaneous maximum. Application memory after a
launch is outside the service's budget. The existing long-lived installed
launcher can have a different footprint from this isolated comparison.

The latency increase is deliberate: loading the helper costs time that the
resident KDE service amortized. There is no persistent warm-helper cache. Only
one helper runs at a time, so bursts queue instead of loading a GUI toolkit per
request. D-Bus activation waits run in Rust after the resolving helper exits;
a nonresponsive D-Bus application does not occupy the helper semaphore.

The real .NET client also passed 30 interleaved measured samples plus warmups.
Its service dispatch median/p95 were 59.4/61.6 ms;
its existing installed one-shot KIO fallback measured 87.9/92.9 ms.
The local raw client report is `artifacts/launcher-rust-dotnet-dispatch-final.json`.
This benchmark uses offscreen Qt; its fallback binary is the installed legacy
helper, as recorded by hash in that report.

Dispatch acceptance is not first presentation or application readiness. The
native benchmark compares the old and new services directly. The separate
managed-client benchmark exercises the real `DaisyLaunchClient`; its installed
KIO fallback and direct execution routes are different backends, not an A/B
substitute for the old resident service. Full shell UI interaction and a fresh
ISO installation were not exercised.

## Behavior and bounds

- The existing D-Bus API version, methods, signals, catalog JSON and CLI entry
  points remain available. KService resolves ordinary/absolute desktop entries
  and action IDs. Missing `TryExec` executables are rejected for catalog IDs.
- Sender UID checks, per-owner request access, idempotency/conflict checks,
  input validation, 16 pending requests per owner, 20 recent requests/sec per
  owner, 1,024 total records, four waiters, 15-second timeout and 120-second
  cleanup are retained. UTF-8 byte limits replace Qt character-count limits.
- Retained request payloads have an additional 8 MiB aggregate budget. This
  prevents maximum-size URI lists from accumulating hundreds of MiB during
  the two-minute idempotency window. Full-budget requests receive `RateLimited`.
  Catalog size is independently capped at 16 MiB. These are data bounds, not a
  claimed bound on total process RSS, allocator overhead or incoming D-Bus frames.
- Only one KDE helper is allowed, including catalog refresh. The Rust executor
  uses one thread. Cancelled queued requests are checked before helper spawn.
  Helper failures do not trigger an ambiguous second launch attempt.
- Activation tokens travel to the helper over stdin; helper result descriptors
  are close-on-exec. Launched applications survive helper/broker exit and cannot
  keep the broker's result pipe open. CLI fallback detaches application stdio
  from command-runner pipes. Traces do not contain paths, URIs or tokens.
- inotify directory changes trigger debounced sycoca/catalog refreshes. A failed
  refresh keeps the last successful catalog. The existing catalog and dispatch
  environment behavior is retained in the KDE helper.

The broker must ship beside `daisy-launch-backend`. Qt/KDE dependencies remain
required at runtime for that helper; Rust is required only for building. The
legacy resident implementation remains available in a separate CMake build
for comparison and rollback testing.

## Validation

The real-KDE private-bus acceptance test passed with telemetry enabled and
explicitly disabled. It covers catalog/localized labels, executable arguments
with spaces, ordinary and absolute desktop files, actions, missing `TryExec`,
URI validation, idempotency, owner isolation, D-Bus Activate/Open/ActivateAction,
nonblocking activation waits, cancellation, live catalog refresh, CLI fallbacks,
private traces and application survival after service exit.

The isolated Rust-broker test uses a fake helper to cover malformed output,
crashed/missing helpers, secret transport over stdin, queued cancellation,
helper reaping, the 16-pending limit, one-helper concurrency, timeout, aggregate
payload budget and SIGTERM cleanup. Rust formatting and Clippy with warnings
as errors passed. The native default build and the real managed client build
also passed.

```sh
bash scripts/build-daisy-launch.sh
python3 scripts/test-daisy-launch.py
DAISYOS_LAUNCH_TELEMETRY=0 python3 scripts/test-daisy-launch.py
python3 scripts/test-launch-broker.py
cargo clippy --manifest-path services/DaisyLaunch/rust/Cargo.toml --release --locked -- -D warnings

cmake -S services/DaisyLaunch -B /tmp/daisy-launch-legacy \
  -DDAISY_LAUNCH_LEGACY=ON -DCMAKE_BUILD_TYPE=Release
cmake --build /tmp/daisy-launch-legacy
python3 scripts/benchmark-launch-memory.py \
  --legacy /tmp/daisy-launch-legacy/daisy-launch \
  --rust services/DaisyLaunch/build/daisy-launch \
  --platform wayland --runs 3 --samples 30 \
  --output artifacts/launcher-rust-wayland-memory.json
```

For headless runs use `--platform offscreen`. The protocol test uses offscreen
Qt and never takes over the running launch service. The Wayland benchmark
connects to the existing compositor but opens no application windows.
