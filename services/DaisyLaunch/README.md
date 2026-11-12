# DaisyLaunch

`daisy-launch` is a Rust session D-Bus broker. It keeps the application catalog
as serialized JSON and retains bounded request state. It does not load Qt/KDE.
A sibling executable, `daisy-launch-backend`, uses KService/KIO on demand for
catalog discovery, desktop-entry resolution, and application dispatch.

Keep both executables in the same directory. The broker resolves its helper
relative to its own executable, rather than searching PATH. The existing local
installer, shell publisher, and ISO staging scripts include both executables.
The running user service is not replaced by a build alone.

```sh
bash scripts/build-daisy-launch.sh
python3 scripts/test-daisy-launch.py
python3 scripts/test-launch-broker.py
cargo clippy --manifest-path services/DaisyLaunch/rust/Cargo.toml --release --locked -- -D warnings
```

CMake builds Rust by default. A separate comparison build can use
`-DDAISY_LAUNCH_LEGACY=ON`; the production build script explicitly selects Rust.
Rust/Cargo are build-time requirements. Existing Qt/KDE runtime dependencies
remain necessary for the helper.

The D-Bus service/path/interface, API version, methods, signals, catalog format,
CLI `--catalog` / `--launch` fallbacks, idempotent request IDs, owner isolation,
per-owner limits, four-waiter limit, 15-second timeout and 120-second request
retention remain supported. The broker validates caller UID and request fields;
KService remains authoritative for desktop files and action IDs. Incoming string
limits are UTF-8 byte limits. Aggregate retained request payloads are additionally
limited to 8 MiB and receive the existing `RateLimited` error when full.

A semaphore limits the broker to one KDE helper at a time, including catalog
refreshes. A worker is reused during a launch burst and is killed/reaped after
approximately two seconds without a request. The idle lease uses a deadline and
notifications; an empty worker pool does not poll. Refreshes retire it first. D-Bus-activatable applications are resolved by KService, then activated
asynchronously from Rust after that helper finishes resolving. Pending activation replies
therefore do not occupy the helper queue or extend the idle lease. Queued requests can
be cancelled before spawning a helper. Cancellation does not promise to undo a
launch that already reached an application.

Activation tokens for service requests travel over a private pipe, not helper
arguments or telemetry. Request and result descriptors are close-on-exec and
application stdin/stdout are isolated from those protocol pipes. A close-on-exec result descriptor prevents launched
applications from holding the helper's output pipe open. CLI fallback launches
also detach application stdio from the command runner's pipes. Tokens passed
explicitly to the legacy-compatible `--launch --token` CLI still appear in that
CLI's arguments; callers should prefer the D-Bus API.

Catalog changes use inotify plus debounced, bounded `kbuildsycoca6`/discovery
jobs. Refresh failure retains the previous catalog. Trace timestamps use the
same Linux monotonic clock as KDE; actual lookup/dispatch stamps are returned
by the helper. `backend_started` additionally records when helper work begins.
No application paths, URIs or tokens are added to traces.

The sub-16-ms warm dispatch result, cold-launch costs and idle-worker tradeoff
are documented in [the dispatch optimization report](../../docs/performance/launcher-dispatch-optimization.md).
The original migration measurements are documented in
[the migration report](../../docs/performance/launcher-rust-migration.md).
