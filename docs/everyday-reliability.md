# Everyday reliability and 8 GB acceptance

The DaisyOS reliability protocol captures crash evidence and resource pressure
without assuming that memory is the cause. The installed DaisyOS image is the
authoritative target. Development sessions are useful supplemental evidence but
do not prove the shipped zram, cgroup, or OOM policy.

Install or repair the host memory-resilience layer with:

```bash
sudo scripts/install-memory-resilience.sh --disk-swap-gib 4
```

This installs zstd zram (up to 4 GiB), a 4 GiB low-priority disk fallback,
systemd-oomd policy, cgroup-v2 health checks, and conservative VM tuning. The
operation is idempotent and refuses unsupported filesystems, insufficient free
space, oversized requests, and pre-existing non-swap files. Disk swap may add
SSD writes and latency only after faster RAM and compressed swap are pressured.

## Commands

Capture the current boot before changing the system:

```bash
sudo -E scripts/daisyos-reliability.py capture \
  --output artifacts/reliability --label before
```

Run the default eight-hour rotation with 30-second samples:

```bash
sudo -E scripts/daisyos-reliability.py run \
  --manifest tests/reliability/everyday-v1.json \
  --duration 28800 --interval 30 \
  --checkpoint-results tests/reliability/checkpoints.example.json \
  --output artifacts/reliability --label integrated-before
```

Use `--duration 86400` for the final 24-hour acceptance run. A run directory is
created beneath the output directory and is never reused. `raw/` contains the
initial evidence, `end/` contains the final journals, `samples.jsonl` contains
time-series measurements, and `summary.json` plus `findings.md` contain the
derived result. Core metadata is collected, but core payloads remain under
systemd-coredump access controls and are never copied into the repository.

Rebuild a report or compare matched runs:

```bash
scripts/daisyos-reliability.py summarize artifacts/reliability/RUN
scripts/daisyos-reliability.py compare \
  artifacts/reliability/BEFORE/summary.json \
  artifacts/reliability/AFTER/summary.json \
  --output artifacts/reliability/comparison.json
```

The command returns nonzero when mandatory evidence is missing, a workload step
fails, an OOM or unexplained crash is present, a hardware/driver reliability
event is found, MemAvailable falls below 1 GiB, memory PSI `full` is nonzero at
a sample, or a repeatedly observed process grows beyond the warmed plateau.
Use `capture --strict` when a point capture must enforce the same gates.

## Workload contract

The checked-in manifest is sanitized and deterministic. Its fixture checks,
paced rotation, and human checkpoints cover productivity/comms, media, and
development/content creation without depending on external accounts or URLs.
For hardware qualification, copy the manifest into the run environment and add
site-specific `command` steps that open and drive the approved browser, office
suite, video-call fixture, player, IDE, editor, and synchronization fixture.

Commands are argv arrays and are never evaluated by a shell. `${FIXTURES}` is
replaced with the run's generated fixture directory. Missing programs and
nonzero exits fail the workload rather than being silently skipped. Supported
step types are `command`, `sleep`, and `checkpoint`.

Copy `tests/reliability/checkpoints.example.json` outside the repository and
replace its placeholders with operator, UTC time, observations, and artifact
references from the actual run. Omit `--checkpoint-results` while unattended;
the report then remains failed with explicit pending checkpoints instead of
claiming interactions were verified.

Optional attribution runs accept `--profiler dotnet-counters`, `heaptrack`,
`valgrind`, or `asan-lsan`. Profilers wrap command steps and must not be used for
the acceptance measurement. A missing profiler causes the run to fail.

## Evidence interpretation

Every classified event retains a measured or inferred basis. SIGSEGV and
SIGABRT establish how a process ended, not why. SIGKILL remains unexplained
unless kernel or systemd-oomd evidence identifies its source. Temporal proximity
alone does not turn GPU, storage, thermal, power, or pressure events into a root
cause.

PSS is the primary process metric. RSS is supplemental. Slab, page tables, page
cache, process PSS, and GPU allocations are separate accounting domains and are
not summed. The growth detector is a triage gate; confirmed candidates require
a matched reproduction and the appropriate managed or native profiler.

## Qualification matrix

Run matched before/after builds on two physical 8 GB laptops: integrated graphics
and discrete graphics. Record the exact DaisyOS build, kernel, firmware, display,
driver, app versions, and manifest hash. Each system needs an 8+ hour baseline
and a 24-hour post-fix mixed rotation.

Acceptance requires zero OOM kills, zero unexplained crashes, no GPU reset,
filesystem/storage error, or thermal shutdown, at least 1 GiB MemAvailable,
no sampled sustained memory PSI `full`, and no unbounded PSS growth. Separately
record clean-boot settled `MemTotal - MemAvailable`; the target is approximately
1.5 GiB. A failed gate remains a failure. Hardware results and pending human
checkpoints must be reported separately rather than generalized.
