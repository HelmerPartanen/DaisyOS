# Phase 7: advanced scheduling and system tuning

## Implemented policy

Phase 7 adds read-only inventory to `scripts/daisyos-gaming-diagnostics` for
BORE/POC/sched_ext kernel support and active sched_ext state, IRQ
affinity/irqbalance, the diagnostics process's CPU mask and hybrid-core
topology, online NUMA nodes and the diagnostics process memory policy, each block
device's current I/O scheduler, and common Mesa/DXVK/VKD3D cache presence. The
report also states which system policies DaisyOS deliberately leaves alone.
It does not alter scheduler, IRQ, affinity, memory, I/O, or shader settings.

The absence of a measured and repeatable gain means there is no phase 7 tuning
to promote to a DaisyOS default. This preserves the upstream scheduler,
device/kernel IRQ placement, kernel-selected CPU placement, first-touch memory
policy, per-device I/O scheduler, and runtime shader-cache behavior.

## Candidate decisions

| Candidate | DaisyOS decision | Required comparison | Rollback |
|---|---|---|---|
| BORE | Do not patch or select by default. Treat it as a separate kernel candidate and record the exact patch revision/config. | Same kernel base/config except BORE; repeated game 1% lows, p99 frame time, desktop response under CPU contention, audio, power and thermals. | Reboot the retained stock kernel entry. |
| sched_ext (`scx`) | Do not install a loader service or auto-start an unselected scheduler. The diagnostic reports kernel capability and active state. | Test one pinned `scx` release/scheduler per run, including boot, suspend/resume, game launch/exit, shell contention and scheduler crash/bypass. | Stop the exact scheduler service/process; kernel sched_ext falls back on scheduler exit/error. Reboot stock kernel if recovery is incomplete. |
| POC selector | Do not add a kernel patch without a pinned upstream revision and isolated build. It changes CPU idle selection and can overlap with other scheduler/kernel changes. | Test only on a supported kernel/CPU with POC as the sole change, and measure wakeup latency, frame tails, throughput, idle power and hybrid placement. | Reboot the stock kernel. |
| IRQ policy | Preserve kernel/device affinity and the platform's existing irqbalance policy. DaisyOS writes no `/proc/irq/*/smp_affinity*` values. | Compare per-device IRQ counters and game frame tails under a representative storage/network load; account for managed IRQs and suspend/resume. | Restore captured affinity masks and irqbalance's prior enablement; reboot if the platform fails to restore them. |
| CPU affinity / hybrid cores | Do not pin games or helper processes. Preserve kernel topology-aware scheduling; diagnostics report core types, sibling groups and the caller's allowed CPU set. | Compare unpinned placement with a workload-specific policy on each CPU topology, including desktop input, audio and background work. | Terminate the per-game scope to drop affinity/cpuset constraints; never write persistent global CPU masks. |
| Memory / NUMA | Keep kernel first-touch and normal reclaim policy. No per-game `mbind`, `numactl`, swappiness or huge-page changes are applied. | For multi-node systems, compare local/remote placement, frame-time tails, page faults, memory pressure and game load time. | Remove the per-process policy and restart the game; no persistent VM setting is introduced. |
| I/O scheduler | Keep each block device's current upstream default. The diagnostic now reports all detected device queues instead of assuming `nvme0n1`. | Compare only on the target storage device under concurrent game asset reads and background I/O; include latency tails and throughput. | Restore each device's recorded scheduler value. |
| Shader compilation | Do not change compiler process priority, shader cache location, or cache eviction. Diagnostics report known cache-directory presence only. | Compare cold and warm shader runs, compile time, stutter/frame-time tails, disk usage and background CPU contention. | Stop the per-game experiment and restore the recorded process priority/environment; leave cache contents untouched. |

## Evaluation workflow

1. Capture `scripts/daisyos-gaming-diagnostics --json` before each candidate.
   Keep the report private until reviewed because it includes local hardware and
   session inventory.
2. Change one variable only, record exact package/kernel/config/service state,
   and keep the stock kernel plus a recovery boot entry available.
3. Alternate baseline/candidate order on the same machine and use at least five
   independent MangoHud frametime logs per side. For example:

   ```bash
   python3 scripts/compare-game-frametimes.py \
     --baseline artifacts/tuning/base-01.csv artifacts/tuning/base-02.csv artifacts/tuning/base-03.csv artifacts/tuning/base-04.csv artifacts/tuning/base-05.csv \
     --candidate artifacts/tuning/candidate-01.csv artifacts/tuning/candidate-02.csv artifacts/tuning/candidate-03.csv artifacts/tuning/candidate-04.csv artifacts/tuning/candidate-05.csv
   ```

4. Treat `inconclusive` or any regression as a no-go. The existing comparator
   intentionally does not certify an overall gaming-performance gate. Also
   capture interaction latency, audio, power and temperature for the relevant
   workload; FPS alone cannot approve a system policy.
5. If a future experiment is accepted, implement it behind an explicit
   per-game opt-in with captured prior state, bounded ownership, cleanup on game
   exit/daemon crash, and a recovery path that does not depend on the desktop
   shell remaining responsive.

## Upstream interface notes

- Linux documents sched_ext's runtime bypass and return to the kernel scheduler
  in the [sched_ext guide](https://docs.kernel.org/scheduler/sched-ext.html).
- IRQ masks are system-wide routing controls; follow the [kernel IRQ affinity
  documentation](https://docs.kernel.org/core-api/irq/irq-affinity.html) and
  account for device-managed interrupts.
- CPU and memory placement constraints interact through cpusets; see the
  [kernel cpuset documentation](https://docs.kernel.org/admin-guide/cgroup-v1/cpusets.html).
- I/O schedulers are selected per block device and vary by device/workload; see
  [the kernel scheduler-switching guide](https://docs.kernel.org/block/switching-sched.html).
- Current experimental references are the upstream [sched-ext schedulers](https://github.com/sched-ext/scx),
  [BORE patch project](https://github.com/firelzrd/bore-scheduler), and
  [POC selector project](https://github.com/firelzrd/poc-selector). Their
  kernel-version support must be checked at the time of any proposed trial.

## Acceptance status

Repository inventory and rollback rules are implemented. No tuning option has
been proven beneficial on DaisyOS hardware, so none is made a default. Hardware
comparisons, including audio, idle power, suspend/resume and crash recovery,
remain required before this phase's performance exit criteria can be accepted.
