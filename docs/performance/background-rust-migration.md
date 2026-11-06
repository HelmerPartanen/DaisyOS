# Background process RAM audit and Rust migration

Measured locally on 2026-11-06, Linux x86-64, rustc 1.99.1, glibc and
libwayland-client 1.26.0. These are measurements on the existing development
session, not a clean boot or a guaranteed deployment budget.

## Result

The idle monitor now builds as Rust by default, both through its existing build
script and CMake. Its library keeps the existing `libdaisy_idle.so` C ABI and
output path. Publishing now explicitly builds and includes this library. The
standalone tool keeps its `IDLE` / `RESUMED` stdout protocol and stdin-EOF lifetime.
No installed services were restarted or replaced during this work.

This is a small physical-memory improvement. **A C-to-Rust rewrite is not a
credible route to tens of megabytes of savings from these C helpers.** The big
resident costs belong to the C#/.NET services and the C++ Qt/KDE launcher.
The idle-monitor change did not migrate those services. A subsequent
[launcher migration](launcher-rust-migration.md) now replaces the resident
Qt/KDE daemon with a Rust broker and an on-demand KDE helper; desktop services
and Continuity remain managed.

## Repeated A/B results

Nine alternating samples per implementation and mode, after 300 ms settling,
with a real Wayland compositor. Both implementations link the same system
Wayland library. The library host is the same minimal C `dlopen` harness for
both implementations; it is not the running Avalonia shell. Raw samples:
[idle-memory-2026-11-06.json](idle-memory-2026-11-06.json).

| Mode / metric | Original C | Runtime-free Rust | Reduction |
| --- | ---: | ---: | ---: |
| Standalone RSS | 2,400 KiB | 1,884 KiB | 516 KiB |
| Standalone PSS | 218 KiB | 183 KiB | **35 KiB** |
| Standalone private dirty | 196 KiB | 164 KiB | 32 KiB |
| Standalone threads | 2 | 1 | 1 |
| Library host RSS | 2,452 KiB | 2,024 KiB | 428 KiB |
| Library host PSS | 274 KiB | 256 KiB | **18 KiB** |
| Library host private dirty | 252 KiB | 236 KiB | 16 KiB |
| Library host threads | 2 | 2 | 0 |

PSS distributes shared pages between processes and is the more useful physical
RAM estimate. RSS counts shared library pages in full. Do not add these RSS
reductions across processes or describe them as equivalent free system RAM.
The small PSS differences can move by several pages between runs.

Standalone virtual size dropped from 78,016 to 3,040 KiB; the library host from
78,036 to 69,628 KiB. **These are address-space reservations, not RAM savings.**
The shell uses the library, so the standalone improvement must not be advertised
as the shell's improvement. No whole-shell before/after reduction is claimed.

An initial standard-library Rust implementation using a native Rust Wayland
backend measured approximately 618 KiB PSS against approximately 222 KiB for C.
It was rejected and replaced. Rust's ownership model does not automatically
reduce working set; dependency/runtime choices matter more here.

## Implementation choices

- `no_std`, no Rust allocator, async executor, logging framework or extra runtime
  thread. A single native handle allocation is freed after joining the worker.
- Use the already-shared system Wayland library. A small C adapter exposes only
  scanner-generated inline protocol functions. Monitor logic is Rust; this is
  not a wrapper that executes the old C monitor.
- The standalone executable runs the event loop on its main thread.
- The C ABI library still needs a worker to avoid blocking the shell. It uses an
  explicit 1 MiB stack instead of the platform pthread default. This primarily
  saves virtual address space. The stack keeps headroom for managed reverse-P/Invoke callbacks; current
  callbacks dispatch UI work or start asynchronous operations. Arbitrary deep
  callback stacks are outside this bounded-stack design.
- Shutdown closes a close-on-exec wake pipe and joins the worker. There is no
  cross-thread `volatile` running flag. Handle ownership and callback lifetimes
  remain the caller's responsibility at this unsafe FFI boundary.
- Event reads use Wayland's prepare/read/cancel discipline. Interrupted polls,
  socket errors and output backpressure are handled.

Startup discovery still uses a synchronous Wayland roundtrip, as the C version
did. A compositor that connects but stops responding during discovery can delay
start/stop; this migration does not claim to solve that existing limitation.
Rust here is Linux-native; the build also requires a C compiler, pkg-config,
wayland-scanner and libwayland-client development headers. Rust is a build-time
requirement only. `Cargo.lock` fixes the sole Rust dependency (`libc`).

## Where the larger gains would come from

Read-only snapshot of existing resident processes during the audit:

| Process | RSS | PSS | Finding |
| --- | ---: | ---: | --- |
| Desktop services (`DaisyOS.Shell --desktop-services`) | ~139 MiB | ~96 MiB | Avalonia/.NET process stays resident to serve portals and authentication dialogs. |
| Continuity daemon (C#/.NET) | ~116 MiB | ~78 MiB | Managed runtime plus D-Bus, discovery, TLS, protobuf and feature state. |
| DaisyLaunch (C++ Qt/KDE) | ~50 MiB | ~21 MiB | QGuiApplication, KService/KIO catalog and launching machinery. |
| PolicyKit helper (C) | ~5.9 MiB | ~0.6 MiB | Most RSS is shared GLib/GIO/PolicyKit. |

These are **current footprints, not predicted savings**. Process sharing, loaded
pages and workload affect every row. The interactive shell is excluded from the
background-service candidates. Run `scripts/profile-background-memory.py` to
inspect the current session without exporting full process command lines.

Priority for a subsequent larger migration:

1. Move the desktop portal D-Bus broker to a small native service and launch the
   existing Avalonia dialogs on demand. That is the largest observed resident
   target. Preserve portal versioning, request cancellation, parent-window
   identifiers, permission/consent and PolicyKit cookie/identity handling. A Rust
   rewrite retaining a permanently resident Avalonia UI would miss the main cost.
2. Port Continuity as a complete interoperable daemon only with its existing
   protocol, pairing, trust storage, revocation, TLS, session gating and transfer
   tests as acceptance criteria. Its 78 MiB PSS is an opportunity ceiling, not a
   promise: the replacement also needs crypto/network buffers and feature state.
3. **Implemented subsequently:** a small Rust launcher broker with KDE launch
   semantics delegated on demand; see the linked launcher report. Measure repeated launches as well as idle memory; activation tokens,
   desktop actions, URI expansion, D-Bus activation and idempotent requests must
   survive. Dropping KIO semantics to reduce idle memory would be a regression.
4. Keep the tiny C helpers unless a measured workload identifies a leak or
   unbounded buffer. PolicyKit's entire observed PSS is smaller than 1 MiB;
   rewriting it while retaining GLib cannot yield large savings. Mail rendering
   is on demand and its WebKit cost remains regardless of host language.
   Continuity media RAM comes largely from GStreamer/codec/frame buffers; measure
   active streams and queue depth before changing its implementation language.

No projected percentage or aggregate megabyte saving is asserted for the
remaining unimplemented architectural changes. The subsequent launcher report
contains measured savings for the implemented launcher migration. The audit establishes priorities and a
repeatable baseline, not an unmeasured claim of a complete daemon migration.

## Reproduce and validate

```sh
bash scripts/build-native-idle-monitor.sh
dotnet build scripts/idle-monitor-acceptance/IdleMonitorAcceptance.csproj -c Release
python3 scripts/test-rust-idle-monitor.py --managed-host \
  scripts/idle-monitor-acceptance/bin/Release/net10.0/IdleMonitorAcceptance.dll
cargo clippy --manifest-path src/native/daisy_background/Cargo.toml --release --locked -- -D warnings

# Separate build directory: production CMake builds Rust by default.
cmake -S src/native/daisy_idle_monitor -B /tmp/daisy-idle-c \
  -DDAISY_IDLE_C_BASELINE=ON -DCMAKE_BUILD_TYPE=Release
cmake --build /tmp/daisy-idle-c
python3 scripts/benchmark-idle-memory.py --baseline-dir /tmp/daisy-idle-c \
  --samples 9 --output artifacts/idle-memory.json
python3 scripts/profile-background-memory.py
```

The isolated protocol fixture validates availability, missing protocol/display,
real marshalled idle/resume callbacks and user data, 50 start/stop cycles with
file descriptor and thread cleanup, stdin bytes/EOF, compositor disconnect and
timeout overflow. It does not move the pointer, lock the desktop, capture the
screen, suspend the machine or take over a running service. Real compositor
connection/discovery and waiting were exercised by the memory benchmark.
A .NET 10 acceptance host also passed 50 reverse-P/Invoke cycles with user data
and process startup on the native worker stack. Full shell publishing and
Avalonia UI callbacks were not exercised.
