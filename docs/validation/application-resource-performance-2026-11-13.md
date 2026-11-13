# Application resource and startup pass — 2026-11-13

This continues the [OS performance target plan](../plans/os-resource-performance-2026-11-13.md).
The first pass's [gaming maintenance result](os-resource-performance-2026-11-13.md)
remains separate from application readiness and memory results.

## Changes retained

- Typography shapes its multilingual metric sample once per application of the
  policy, then scales the design metrics for each role. No global metric cache
  or culture-dependent shortcut was added. The same fonts, line heights, text
  sizes, controls and accessibility scales remain available.
- Notes creates each editor only when its document type is first opened. Empty
  windows create neither; rich-note windows initially omit the code editor;
  plain-text/code windows initially omit the rich formatting UI. Editors remain
  cached after use so switching does not repeatedly construct controls or change
  the existing undo behavior.
- The code editor installs TextMate only for code or Markdown. Plain text and an
  unused editor create no syntax registry. Switching back to plain/binary text
  or detaching disposes the installation; reattachment restores it when needed.
- `DAISYOS_STARTUP_TRACE=1` enables stage timestamps, managed allocation totals
  and Gen2 counters. Normal runs have tracing disabled. Serialization is source
  generated and introduces no trim/AOT analyzer warnings. The OS comparator now
  rejects a comparison whose tracing settings differ.

Compiled launcher bindings and changes to synchronous page layout were tested
and removed: these experiments did not establish a repeatable useful gain. The
launcher behavior remains unchanged; its search target is still outstanding.

## Typography measurement and parity

Release, headless Skia, two discarded warmups and 30 measured calls to the real
`TypographyPolicy.Apply`. These are warm updates, excluding cold font startup.

| Metric | Before | After | Reduction |
| --- | ---: | ---: | ---: |
| Median policy update | 1.778 ms | 0.209 ms | 88.2% |
| Median current-thread allocation | 127,400 bytes | 23,088 bytes | 81.9% |

The final all-font check agrees with the independent size-by-size reference on
**1,050,237** combinations across **1,913** installed font families, nine roles
and every integer scale from 90% to 150%. The quick regression set checks nine
families, including the three integer-boundary cases (4,941 combinations). These
quick parity checks also run in the UI suite. The expanded check caught 24
one-pixel rounding differences in the initial scalar-height prototype. The final
policy scales each fallback face’s raw ascent, descent and line gap in the same
order as the original text formatter, preserving those edge cases. This does not imply an 82% reduction in process memory.

Reproduce using `scripts/typography-performance/README.md`.

## Notes startup and attributable memory

`benchmark-app-startup-resources.py` alternated baseline and candidate order,
with two warmups and seven measured launches per variant. Both variants used
identical Release Notes entry assemblies, runtime dependencies, isolated data
and appearance. Only the saved Shell assembly varied. The fixture contained 20
plain-text notes. The desktop/IDE remained open; this is not a controlled idle
or fresh-login capture.

| Metric | Baseline median (MAD) | Candidate median (MAD) | Change |
| --- | ---: | ---: | ---: |
| Initial-render readiness | 2,195.1 ms (33.9) | 1,955.6 ms (28.9) | −10.9% |
| PSS at readiness | 192.14 MiB (0.19) | 182.27 MiB (0.11) | −9.87 MiB / −5.1% |
| Private dirty at readiness | 110.97 MiB | 104.54 MiB | −6.44 MiB / −5.8% |

Readiness ranges were 2,149–2,229 ms before and 1,881–2,002 ms after. The
boundary is parent `dotnet` launch to the authenticated render-thread-ready
signal, followed immediately by a `/proc` resource read. It is not physical
presentation, peak startup memory or settled memory. This diagnostic is separate
from the fixed eight-workload OS score, which uses apphosts.

The earlier syntax-only comparison showed 186.1 → 177.7 MiB median PSS in its
own paired run. Absolute PSS changed between capture sessions because sharing
and other desktop activity changed. Compare the matched variants within each
report, rather than subtracting across sessions. Thread-count medians were
unchanged in the syntax-only run; no thread-count gain is claimed.

The final paired report is `notes-final-fidelity-resources.json`; its candidate
assembly hash exactly matches the all-font parity report and all seven UI
Release output directories after the full solution build. Local raw reports,
binary hashes, runtime manifests and logs are retained in
`artifacts/performance-2026-11-13/`. The original baseline assembly is retained
under `startup-memory-baseline/DaisyOS.Shell.dll`. Reproduce against a new build:

```sh
python3 scripts/benchmark-app-startup-resources.py \
  --baseline-shell artifacts/performance-2026-11-13/startup-memory-baseline/DaisyOS.Shell.dll \
  --candidate-shell src/DaisyOS.Shell/bin/Release/net10.0/DaisyOS.Shell.dll \
  --output /tmp/notes-startup-resources.json
```

The 500 ms Notes readiness budget and the 10% PSS reduction target remain unmet.
The controlled desktop idle-memory/CPU and game frame-time gates remain pending.
No games are installed on this laptop.

## Verification

- 101 Release UI tests passed, including typing, rich formatting/undo, switching
  editors without cross-document writes, lazy construction, highlighting
  teardown/reattachment, language/scale layout, launcher paging, icon lifecycle
  and motion.
- 14 fixed OS benchmark protocol tests passed, including the tracing guard.
- 3 new resource-runner tests passed: isolated dependency selection, required PSS
  coverage and raw-sample variability summaries.
- Final typography harness: zero mismatches in all 1,050,237 installed-font combinations.
- Production builds and working-tree whitespace checks pass.

The first lazy-editor lifecycle test inspected an unarranged cached control's
visibility. It now completes layout at each document transition, verifies that
the selected editor is actually visible, and verifies that the previous editor
is hidden. The complete suite passes with that correction.

## Installed session configuration

The active session is explicitly configured **Debug** in
`/etc/daisyos/daisyos-native-development.conf`, and its running supervisor pins
the Debug executable. The checkout’s `bash scripts/daisyos-native-development status` now shows the
active build configuration. A Release configuration with the same checkout path is
prepared locally at
`artifacts/performance-2026-11-13/native-development-release.conf`.

Applying that file requires administrator access; `sudo -n` reports that a
password is required. The current supervisor also needs a new login to use the
new configuration. The installed configuration and running desktop have not
been changed. After administrator review, preserve the old file and install
the prepared one, then select the native development session at the next login:

```sh
sudo cp -a /etc/daisyos/daisyos-native-development.conf /etc/daisyos/daisyos-native-development.conf.pre-performance
sudo install -m 0644 artifacts/performance-2026-11-13/native-development-release.conf /etc/daisyos/daisyos-native-development.conf
```

Do not treat the source-build benchmarks as evidence that the active Debug
shell has gained these improvements. Capture three matching Release fresh
logins before certifying desktop idle targets.

## Fixed OS workload follow-up

A live taskbar-size preference changed from 2 to 1 between the initial and final
captures. The strict comparator rejected that pairing. `--appearance-file` now
snapshots the baseline JSON into the benchmark's private configuration, without
changing the running desktop. It validates the input and reads it once so the
recorded values match the child configuration. The protocol tests cover missing,
invalid and exact snapshot inputs.

The matched follow-up completed all eight workloads. The score changed from
691.8 to 825.9 in that capture, but large Files microbenchmark variation dominates
part of that score change despite no Files optimization. This does not establish
a 19.4% general OS improvement. Notes median readiness was 2,000 ms, consistent
with the separately observed startup gain. Launcher hide/show p95 increased from
23.3 to 31.2 ms while its median changed from 19.6 to 19.9 ms. The short/noisy
suite cannot certify the full latency regression gate; use controlled 30-sample
captures for acceptance. The source-level launcher experiments were reverted.

The revised hidden-memory settings label and description have both neutral and
Finnish catalog entries. The catalog verifier exposed **44 pre-existing missing
Finnish entries**. This pass preserves that exact missing-key set and adds only
the two new translated messages; it does not claim a clean global localization
check. Details are retained in `localization-baseline-gaps.json`.

A 30-second read-only diagnostic of the running Debug session recorded about
6.7% of one CPU in the shell and 0.5% in DaisyOS services in its last interval.
It overlapped active development/benchmark work and cannot certify idle CPU.
Compositor PSS was inaccessible, so its partial ~290 MiB desktop PSS sum cannot
certify the 700 MiB budget either. The raw report explicitly marks incomplete
coverage; no missing compositor memory was treated as a measured zero.

The final full solution Release build completed with zero errors (six existing
warnings in test projects). A second matched eight-workload capture scored
796.8, with Notes median readiness at 1,966 ms. The two matched follow-ups score
differently despite identical application behavior, reinforcing the limitation
of claiming a general OS percentage gain from this active developer session.
The final rounding-fidelity correction was then verified in the paired Notes
resource run and the 101-test UI suite.
