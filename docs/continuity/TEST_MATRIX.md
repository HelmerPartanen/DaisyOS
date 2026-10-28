# Continuity validation and rollout

## Foundation checks (2026-10-28)

Host: Linux arch-x64, .NET SDK 10.0.113/runtime 10.0.13. No network benchmark
or real two-device acceptance was performed. These tests exercise only isolated
core logic and schema syntax, not cryptographic trust or desktop integration.

| Command | Observed result |
| --- | --- |
| `dotnet build DaisyOS.sln --no-restore -v quiet` | Exit 0, 68 warnings, 0 errors; initial solution build, 2m04s |
| `dotnet test tests/DaisyOS.Core.Tests/DaisyOS.Core.Tests.csproj -v normal` | Exit 0; 22 passed, 0 failed; includes 20 new Continuity cases and 2 existing desktop-grid cases |
| `protoc --proto_path=protocol --descriptor_set_out=/tmp/daisyos-continuity.pb continuity/v1/continuity.proto` | Exit 0; schema parses |
| `git diff --check` | Passed |

The initial Core `--no-restore` invocation produced no test output because the
project had not been restored. It is not counted as verification. A restoring
run then actually executed all 22 tests. Core.Tests was not initially listed in
DaisyOS.sln; this change adds it so the solution gate includes these tests.

Core coverage: disabled/paused state, inactive/locked session, untrusted/revoked
peer, unauthenticated connection, missing capability, missing/expired local file
consent, later/unknown capabilities, sequential frames, clean EOF, truncated
header/body, zero/oversized/overflow lengths, maximum payload, rejected oversized
write and cancellation. No test creates real trust or touches the clipboard.

## Reproducible manual foundation check

```sh
dotnet test tests/DaisyOS.Core.Tests/DaisyOS.Core.Tests.csproj -v minimal
protoc --proto_path=protocol --descriptor_set_out=/tmp/daisyos-continuity.pb continuity/v1/continuity.proto
dotnet build DaisyOS.sln --no-restore -v quiet
git diff --check
```

Review the source integration table against the checkout. In an existing disposable
KWin session, launch the normal shell using the documented developer commands;
open Settings and Files, copy/paste between ordinary apps, lock/unlock, and verify
existing LocalSend still requires receive consent. This manual graphical regression
has not been run here. No Continuity toggle or listener should appear from this
foundation. The full release gate remains `scripts/test.sh`; it has not been run.

## Required gates for remaining milestones

| Milestone | Local automated gate | Manual/hardware gate | Status |
| --- | --- | --- | --- |
| 0 audit/design | Policy/frame tests and schema parse | Source-path review and normal desktop regression | Partial: mock peers, generated bindings and full regression still outstanding |
| 1 service/IPC | Two isolated mock instances, Hello, cancellation, deterministic reconnect time, D-Bus owner loss | Stop Settings; daemon remains responsive; logout cancels | Not implemented |
| 2 discovery/QUIC | Dedup, scoped IPv6, Avahi restart, network changes, bounded retry | Wi-Fi↔Ethernet, internet unplugged; multicast blocked/manual address | Not implemented |
| 3 trust | MITM/forged certificate/key change/replay/downgrade and revoke races | Compare and approve on both devices; reconnect after reboot | Not implemented; release blocker |
| 4 clipboard | UTF-8, limits, echoes, simultaneous copies, lock/revoke cancellation | Native/ordinary Wayland apps both directions; no payload logs | Not implemented |
| 5 files | Traversal/symlink/overflow/disk-full/cancel/digest/resume tampering | Multi-GB send, consent, verified resume, no UI stalls | Not implemented |
| 6 input | Edge mapping/scaling, held-state release, stale sequence, grants | Emergency local shortcut under dead daemon/network; lock/grab/hotplug | Deferred release 2 |
| 7 handoff | App allowlist, schema/size, stale/duplicate/incompatible activity | Supported installed apps; missing resource asks for file transfer | Deferred release 2 |
| 8 rollout | Clean install/upgrade/migration/revoke/rollback | Accessibility, localization, idle CPU/memory, two-device suite | Not implemented |
| 9 video | Separate research artifacts | Codec latency/power, virtual-output feasibility | Optional, deferred |

For each live milestone test IPv4-only/dual-stack, VPN presence, client isolation,
firewall rejection, router reboot, Wi-Fi roaming, suspend/resume, two users/sessions,
service crash and version skew. Fuzz malformed envelopes and parser limits.
Performance reports must include hardware, network, competing load and measurement
method. The master-plan latency/throughput numbers are targets, not results.

## Exact changed files

- `DaisyOS.sln`
- `docs/continuity/MASTER_PLAN.md`
- `docs/continuity/REPO_INTEGRATION.md`
- `docs/continuity/PROTOCOL.md`
- `docs/continuity/SECURITY.md`
- `docs/continuity/OPERATIONS.md`
- `docs/continuity/TEST_MATRIX.md`
- `protocol/continuity/v1/continuity.proto`
- `src/DaisyOS.Core/Continuity/ContinuityPolicy.cs`
- `src/DaisyOS.Core/Continuity/ContinuityFraming.cs`
- `tests/DaisyOS.Core.Tests/ContinuityTests.cs`

## Broader regression findings

`dotnet test --no-build -v minimal` was also run against the initial solution
build. It is not a green gate:

- DaisyOS.Shell.Tests: 177 passed, 4 failed (video wallpaper fixture/playback,
  including missing `Avalonia.Platform.IPlatformRenderInterface`).
- DaisyOS.Tests: 548 passed, 6 failed, 1 skipped (four input-hardware cases,
  desktop shortcut launch, Wi-Fi Settings source contract).
- DaisyOS.AppStore.Tests: 35 passed, 4 failed (three installation-scope argument
  cases and an empty installed-card workflow collection).
- DaisyOS.ShellDemo.Tests reported three installation-scope argument failures;
  completion status is recorded below once the runner exits.

These failures are outside the new isolated policy/codec. No source in those
features was edited, but a clean independent baseline comparison has not been
performed, so this report does not assert that all failures are pre-existing.
Do not treat the full milestone 0 acceptance gate as passed.

Focused test output:

```text
Passed!  - Failed: 0, Passed: 22, Skipped: 0, Total: 22
```

Local detailed logs are retained under `artifacts/continuity/` (ignored build
artifacts). No UI/network/security/performance acceptance is inferred from them.

Final solution build (after registering Core.Tests): exit 0, 68 warnings, 0 errors,
54.19 seconds. The focused 22-test rerun passed again.
The broader runner was interrupted with SIGINT after roughly three minutes once
the gate was already red; ShellDemo additionally reported motion reversal and
four desktop-rename layout failures. Its remaining cases were not completed.
This is an incomplete failing regression run, not a passed full-suite result.

The change contains no daemon, UI activation or new package dependency. Remaining
milestone 0 work includes a mock peer/clock abstraction and generated cross-language
bindings. All live milestones still require their separate implementations and
acceptance evidence before enablement.

## Second increment: peer lifecycle (2026-10-28)

New files:

- `src/DaisyOS.Core/Continuity/ContinuityPeerSession.cs`
- `tests/DaisyOS.Core.Tests/ContinuityPeerSessionTests.cs`

Updated documentation: `PROTOCOL.md`, `REPO_INTEGRATION.md`, `TEST_MATRIX.md`.

Twelve new test cases cover two isolated peers exchanging Hello, transport identity
mismatch, incompatible major version, spoofed Hello identity, deterministic
reconnection/backoff cancellation, revoke during a deliberately late handshake,
dispose before start/repeated disposal, bounded state-feed completion, rejection
of noncanonical/self identities, masking unknown capability bits, active-connection
cleanup, immediate idempotent stop, concurrent duplicate run and already-cancelled startup. Mocks are test-only;
no real TLS, pairing or live network acceptance is implied.

Manual/repeatable script:

```sh
dotnet test tests/DaisyOS.Core.Tests/DaisyOS.Core.Tests.csproj --no-restore -v minimal
dotnet build DaisyOS.sln --no-restore -v quiet
git diff --check
```

The mock retry scheduler releases attempts explicitly; no wall-clock sleeps are
used to drive retry tests. Ten-second test timeouts detect deadlocks rather than
serving as timing assertions. Existing graphical regression failures recorded
above are not fixed or reclassified by this change. Do not enable live sharing.

Second-increment results: focused Core tests exited 0, **34 passed, 0 failed,
0 skipped** (125 ms test duration). Solution build exited 0, **68 warnings,
0 errors** (2m03.26s). `git diff --check` passed. The existing full graphical
suite was not rerun; its previously recorded failures remain unresolved.
Logs: `artifacts/continuity/peer-session-tests.log` and `peer-session-build.log`.
