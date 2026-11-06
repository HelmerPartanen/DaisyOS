# Launcher dispatch below 16 ms

This follows the Rust migration. The target is client request to **dispatch
acceptance**, not first application frame, media streaming, or application
readiness. The installed user service was not restarted during this work.

## Cause

The original memory-saving broker started and destroyed Qt/KDE for every
request, even closely spaced requests. Its real .NET-client traces showed
roughly 30–35 ms of helper preparation and 25–27 ms in backend dispatch. That
explains why dispatch rose from the previous resident launcher's ~24 ms to
~65 ms even though idle RAM improved.

A new `environment_ready` trace stamp isolated another cost: a warm worker
still spent about **12.8 ms** preparing the display environment, against about
**1 ms** in actual KIO submission. This session had a compositor but no XWayland
child. The old function repeatedly read every process's status to confirm the
absence of that child.

## Changes

- Keep a single KDE worker for a **two-second idle lease**, with newline-framed
  requests and bounded result lines. A ready handshake is required. New request
  objects are discarded after each result, rather than accumulating a second
  idempotency history in Qt. The normal request limits remain in Rust.
- Revalidate the known compositor/XWayland process arguments. For lazy XWayland,
  inspect only `/proc/<kwin>/task/*/children`, covering children from all KWin
  threads. A changed/missing process falls back to discovery. There is no
  negative cache that hides a newly started XWayland child. Display/authority
  selection still uses the owning compositor and existing credential checks.
- Cache validated caller UIDs by unique D-Bus sender name, bounded at 256 names.
  A bus disconnect terminates the broker, so entries cannot survive a bus
  lifetime. Request ownership checks remain separate and unchanged.
- Drop/kill a worker when an in-flight transaction is cancelled or times out,
  preventing an unread reply from being consumed by a later request. Reap idle
  workers, restart expired workers on demand, and retire a worker before catalog
  rebuild/discovery. No ambiguous automatic launch retry was added.
- Isolate both request and result pipes with close-on-exec descriptors. Apps
  cannot consume future protocol requests or keep result pipes open. KIO may
  provide its own application stdin pipe; the test compares it against the
  broker's actual pipe identities instead of assuming it must be `/dev/null`.

## Results

Measured on 2026-11-06, Linux x86-64, rustc 1.99.1 (58cf8f9c3 2026-10-13), Qt 6.12.0.
Raw measurements, stage traces and executable hashes:
[launcher-dispatch-optimized.json](launcher-dispatch-optimized.json).

| Metric | Previous one-shot Rust broker | Optimized leased worker |
| --- | ---: | ---: |
| Warm dispatch median | 67.3 ms | 4.6 ms |
| Warm dispatch p95 | 76.1 ms | 5.3 ms |
| First/cold dispatch median | 58.6 ms | 37.5 ms |
| Broker PSS after worker retirement | 1,620 KiB | 1,720 KiB |
| Process-tree PSS immediately after burst | 1,620 KiB | 14,837 KiB |
| Sampled peak process-tree PSS | 16,846 KiB | 16,614 KiB |

**Warm median: 4.6 ms; p95: 5.3 ms.**
Median helper preparation fell from 35.95 ms to
0.37 ms. Optimized warm environment setup measured
1.82 ms and KIO submission 1.36 ms.

The comparison is **before/after Rust brokers**, not the older resident Qt
service. Both use real KService/KIO and the same private bus/XDG fixture with
Wayland connections. The fixture is a windowless `/bin/true` application.
Three alternating-order runs per implementation, three warmups and 30 measured
requests per run were used. Requests are separated by 60 ms, within the lease.
The raw report includes traces, first-dispatch times, binary hashes, process-tree
PSS during the burst, and broker PSS after worker retirement.

**Warm requests meet the 16 ms target in this workload. Cold requests do not.**
After the lease expires the next request still pays for Qt/KDE startup. The
first request is recorded separately rather than being presented as a warm
result. A universal cold-launch guarantee below 16 ms would need a different
tradeoff, such as retaining KDE longer or bypassing parts of its launch path.
Neither is claimed here. Scheduling, queueing, application replies and workload
also affect tails; these figures are measurements, not hard realtime bounds.

There is an explicit short-lived RAM tradeoff. During a launch burst the
broker plus leased helper used about 14.5 MiB PSS, compared with the previous
broker's 1.6 MiB once each helper had already exited. After approximately two
idle seconds the worker was reaped and broker PSS was 1,720 KiB
(1.68 MiB), versus 1,620 KiB before. Do not describe warm latency
as being delivered with only the broker's idle memory footprint. The worker
remains loaded during bursts, and real applications have their own memory
cost outside this process-tree comparison.

The real .NET `DaisyLaunchClient` also passed 30 interleaved measured requests
plus warmups. Service dispatch median/p95 were **4.0/6.3 ms**.
Its helper preparation median was 0.49 ms; backend dispatch median was
2.60 ms. The raw managed-client report is available locally at
`artifacts/launcher-dispatch-optimized-dotnet.json`. That test uses offscreen Qt;
its installed one-shot fallback remains a separate backend, not this comparison
baseline.

## Validation

Real-KDE acceptance passed with telemetry enabled and disabled: catalog and
localized labels, argument quoting, ordinary/absolute desktop entries, actions,
TryExec, URI checks, idempotency, caller isolation, D-Bus Activate/Open/actions,
cancellation, nonblocking activation waits, catalog refresh, CLI fallbacks and
application survival. It also checks that application stdin is not one of the
broker's protocol pipes.

The fake-helper suite passed failure/timeout/cancellation handling, resource
limits, single-helper concurrency, reuse, idle retirement, fresh PID after
expiry, missing/crashed/malformed helpers, payload budget and shutdown cleanup.
Rust formatting, Clippy with warnings as errors, Python compilation and the
native production build passed. The full shell UI and actual media workflows
were not exercised.

Reproduce with `scripts/build-daisy-launch.sh`, `scripts/test-daisy-launch.py`,
`DAISYOS_LAUNCH_TELEMETRY=0 python3 scripts/test-daisy-launch.py`, and
`scripts/test-launch-broker.py`. For memory/latency comparison, pass a preserved
pre-optimization broker plus its sibling helper to:

```sh
python3 scripts/benchmark-launch-memory.py \
  --legacy /path/to/before/daisy-launch \
  --baseline-name 'Rust broker with one-shot KDE helper' \
  --rust services/DaisyLaunch/build/daisy-launch \
  --platform wayland --runs 3 --samples 30 \
  --output artifacts/launcher-dispatch-optimized.json
```

The historical `legacy` JSON key means the supplied comparison baseline; the
report's `comparisonBaseline` field identifies it. Keep the helper sibling with
each binary when reproducing the test.
