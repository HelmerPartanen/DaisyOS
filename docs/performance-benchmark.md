# DaisyOS performance benchmark

Run this inside the DaisyOS graphical session. It produces one score (higher is
better), raw samples, median/p95 timings, and median absolute deviation (MAD).
Keep the laptop plugged in, use the same power profile/display settings, and
close unrelated heavy work. Builds finish before timing starts. Run the same
suite before and after a change, ideally several times to check repeatability.

```bash
# Create a baseline. Release is the default; Debug results are a separate profile.
python3 scripts/benchmark-os-performance.py run --build --output artifacts/performance/baseline.json

# After an optimization, rebuild and measure the same workload.
python3 scripts/benchmark-os-performance.py run --build --output artifacts/performance/after.json
python3 scripts/benchmark-os-performance.py compare artifacts/performance/baseline.json artifacts/performance/after.json
```

Default: two discarded warmups and seven measured runs per workload. For better
tail estimates, use `--samples 30 --warmups 3` for **both** baseline and after.
At seven samples, nearest-rank p95 is the slowest sample; do not interpret small
changes as significant when MAD or repeated-run variation is comparable.
Use `--configuration Debug` for development builds. Reuse built binaries by
omitting `--build`; the report records binary hashes. Existing reports are never
overwritten. Adjacent `.logs/` directories retain build and per-process logs.

## Fixed workloads (protocol v1)

| Metric | Workload | Reference latency | Weight |
| --- | --- | ---: | ---: |
| startup.settings | Fresh native process; initial System page loaded and rendered | 750 ms | 2 |
| startup.notes | Fresh native process; 20 fixture notes loaded and rendered | 500 ms | 2 |
| startup.files | Fresh native process; 1,000 fixture files loaded and rendered | 750 ms | 2 |
| launcher.search | 2,000 synthetic apps; search projection + headless Skia render tick | 16 ms | 1 |
| launcher.show-hide | Same catalog; hide/show + headless Skia render ticks | 32 ms | 1 |
| files.enumerate | Enumerate and prepare 1,000 fixture files using the real Files model | 40 ms | 1 |
| files.filter | Filter the loaded model without re-enumerating | 16 ms | 1 |
| files.sort | Sort the loaded model by size without re-enumerating | 16 ms | 1 |

Each metric's effective latency is `0.7 × median + 0.3 × p95`. The score is:

```text
1000 × exp(sum(weight × log(reference latency / effective latency)) / sum(weight))
```

The reference latencies are fixed scoring budgets, not measured baselines or
claims about competing desktops. Matching all budgets gives 1,000 points;
halving every latency gives 2,000. There is no ceiling. Missing, failed or
invalid workloads produce **no score**, so skipping a slow operation cannot
improve the result. Changing fixtures, weights or timing boundaries requires a
new protocol version.

## Timing boundaries and isolation

Startup measures the parent's monotonic elapsed time from process launch until
an authenticated ready signal from that exact child. The signal is emitted
after fixture/initial page readiness, layout, and Avalonia composition batch
**render-thread completion**. Its cost includes signal-file IO and at most about
2 ms of polling delay under normal scheduling. It does not measure physical
scanout, KWin presentation, user-input dispatch, boot time or animation FPS.
The other five workloads run in a deterministic headless harness; they are
reported separately from the live startup measurements.

Processes start fresh; OS filesystem caches are warmed by discarded warmups.
This is **not a cold-boot/disk-cache benchmark** and never flushes system caches.
Temporary XDG configuration/state/cache, synthetic Files/Notes data, unique IPC
names and separate benchmark window identities protect existing apps and user
data. Notes never reads or writes the user's notes. The benchmark closes only
its own windows and cleans up its own child processes on failures.

Comparisons reject different protocols, fixtures, sample counts, warmups, build
configurations or machine/display/theme environments. Hardware, kernel, GPU,
KWin/.NET versions, CPU governor, startup load and appearance are recorded.
`--allow-environment-differences` prints an explicitly qualified comparison when
hardware/software settings intentionally differ. Binary hashes may differ:
that is expected when measuring an optimization. The comparator recomputes the
score from raw samples instead of trusting saved score/summary fields.

The older `benchmark-shell-performance.sh` remains a resource/soak diagnostic;
`report-responsiveness.py` remains the opt-in trace summarizer. Neither is
silently mixed into this fixed score.

Protocol checks: `python3 scripts/test-os-performance.py`.

During validation, fresh Files processes exposed a bundled/system HarfBuzz ABI
collision. Normal Linux startup now loads the bundled library with local deep
binding before font initialization, using portable Linux native-asset paths
when the runtime reports a distro RID such as `arch-x64`. This preserves the
bundled text renderer for both normal apps and benchmarks. The loader behavior
is documented in the [Linux dlopen manual](https://man7.org/linux/man-pages/man3/dlopen.3.html).

## Google Chrome startup

The runner now measures visible Google Chrome by default, with the same sample and warmup counts. Use `--chrome-binary /path/to/google-chrome` for a custom installation, or `--skip-chrome` to explicitly omit it. Missing or failing Chrome makes the requested benchmark fail.

Chrome uses a new temporary profile for each launch and a fixed loopback page with no external resources. Timing ends when the runner receives the page's first-contentful-paint notification. This includes local callback delivery and is not compositor presentation. It measures fresh-profile startup, not an existing user profile with extensions. OS caches remain warm. Only the dedicated benchmark process group is closed.

Chrome's raw samples, median, P95, protocol and browser version are stored under `thirdPartyMetrics.startup.chrome`. The original OS score retains its eight workloads so existing baseline scores remain comparable. `compare` shows Chrome latency changes when both reports contain the same Chrome version and workload. Older reports have no Chrome result; the next full run establishes that baseline.

Profile isolation follows [Chromium's separate data directory guidance](https://www.chromium.org/developers/creating-and-using-profiles/).

## Diagnose desktop and taskbar launch overhead

The main startup benchmark launches Release binaries directly with isolated data.
The dispatch benchmark measures backend acceptance, which is separate from app
readiness. Live development shortcuts also default to Release so the build
configuration matches the benchmark. Real app profiles, restored windows and
third-party extensions are not represented by the isolated fixtures.

Compare the same GUI readiness signal through direct execution, desktop/GIO and
the persistent KIO service:

```bash
python3 scripts/benchmark-launch-routes.py --app settings --samples 3 --output artifacts/performance/gui-routes-after.json
```

Requires dbus-python and the live DaisyLaunch service. It temporarily registers
a uniquely named fixture desktop entry, uses isolated app data, and removes the
entry afterward. Only fixture windows close. It measures process/dispatch to
initial content rendered; pointer-click delay and physical presentation need a
separate live check. Reports record each route independently and do not alter
the OS performance score.
