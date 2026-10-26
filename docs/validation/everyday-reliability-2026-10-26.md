# Everyday reliability investigation — 2026-10-26

## Status and limits

The evidence pipeline and the production app-memory boundary are implemented.
The two-laptop installed-image matrix and its 8/24-hour runs have not been run,
so this report does not certify DaisyOS on 8 GB hardware and has no fabricated
before/after result. Measurements below are from the supplemental development
host: 7.08 GiB kernel-reported RAM, a development session, and no active swap.

## Measured findings

The current boot journal contains four global kernel OOM victim selections:

- Electron PID 17690 at 18:57 and PID 18756 at 19:11.
- `VBCSCompiler` PID 37688 at 19:17, with approximately 985 MiB anonymous RSS
  and 76 MiB shared RSS reported by the kernel at termination.
- Electron PID 40018 at 20:01.

`/proc/swaps` was empty and systemd-oomd reported that no swap degrades its
memory-pressure handling. These facts establish memory pressure as the cause of
those four terminations on this host. They do not establish that all reported
application crashes are OOMs or that the installed image, which declares zram,
behaves the same way.

Independent failures were also measured. DaisyOS desktop services repeatedly
terminated with SIGABRT during session startup. `daisy-launch` stacks terminate
in Qt platform-integration initialization; DaisyOS.Shell stacks terminate in
CoreCLR but do not contain sufficient symbols to identify the managed caller.
Spotify produced five SIGSEGV core dumps in this boot. Those failures are not
attributed to memory pressure without a matched timeline and symbolized stacks.

A later point sample measured 2,159 MiB MemAvailable and 5,095 MiB
`MemTotal - MemAvailable`. Memory PSI `full` avg10/avg60/avg300 was
0.92/5.93/3.42 percent. Kernel accounting was 218.8 MiB slab, 58.8 MiB page
tables, and 824.1 MiB page cache. Swap use was zero. The GPU exposed 425.6 MiB
VRAM use and 654.9 MiB GTT use; these are separate accounting domains and are
not added to process PSS.

| Rank | Process | PID | PSS MiB | Private MiB | RSS MiB |
| ---: | --- | ---: | ---: | ---: | ---: |
| 1 | spotify | 5422 | 300.5 | 288.8 | 328.9 |
| 2 | spotify | 5241 | 275.4 | 267.0 | 291.1 |
| 3 | DaisyOS.Shell | 96926 | 274.9 | 272.1 | 280.6 |
| 4 | electron | 107201 | 273.8 | 250.6 | 320.2 |
| 5 | electron | 107134 | 261.2 | 236.5 | 309.2 |
| 6 | codex | 107286 | 210.8 | 210.8 | 210.8 |
| 7 | DaisyOS.Shell | 3519 | 178.8 | 178.1 | 179.9 |
| 8 | electron | 107182 | 173.7 | 168.4 | 194.8 |
| 9 | agy | 107715 | 102.0 | 102.0 | 102.1 |
| 10 | electron | 107504 | 100.7 | 100.4 | 103.6 |

This is a single non-idle sample, not an idle budget or leak result. Multiple
processes with the same executable remain separate rows so ownership is not
invented from a name alone.

## Root cause fixed in source

The shipping image configured zram and systemd-oomd but its production session
did not create/configure `daisyos-apps.slice` and did not export
`DAISYOS_MEMORY_APP_SLICE`. Consequently `SafeProcessLauncher` skipped the app
cgroup path and the documented early-OOM boundary was ineffective.

The production supervisor now derives the existing tier policy from MemTotal,
creates the user app slice, sets memory accounting, `MemoryHigh`, and the 60%
systemd-oomd pressure policy, and exports the slice only after configuration
succeeds. At the 8 GB tier this is a 6,656 MiB app high watermark with 1,536 MiB
reserved headroom. Failure remains visible in the session log and does not turn
an optional policy failure into a desktop crash.

This is a source-level correction with automated staging coverage. Its runtime
effect remains unverified until a rebuilt installed image proves zram is active,
apps enter the slice, and pressure selects an app without killing KWin or the
shell. The tradeoff is earlier throttling or termination of memory-heavy apps in
exchange for desktop responsiveness; the boundary is not a per-app fairness
policy.

## What remains unverified

- Clean-boot idle use below approximately 1.5 GiB.
- A stable PSS plateau and absence of leaks during the fixed workloads.
- Integrated- versus discrete-GPU behavior and GPU-reset correlation.
- Storage, thermal, power, suspend/resume, and filesystem behavior over 24 hours.
- Root causes for the startup ABRT and Spotify SIGSEGV groups.
- Zero OOM kills and zero unexplained crashes on both installed-image laptops.

The checked-in harness records these as failures or pending evidence rather than
treating missing measurements as passes.
