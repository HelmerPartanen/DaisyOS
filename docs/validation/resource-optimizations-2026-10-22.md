# First resource-optimization implementation — 2026-10-22

This implements the first cache, idle-work, and session-configuration changes from
[the resource audit](resource-audit-2026-10-22.md). It is not completion of all
resource budgets or proof of improved RDR2 performance.

## Behavior changes

- Speculative application prefetch is disabled by default. Set
  `DAISYOS_PREFETCH=1` explicitly to opt in. The disabled shell does not load or
  record the prefetch catalog, inspect application maps for it, or schedule
  readahead on mouse activity. Existing gaming/fullscreen suspension is retained.
- Memory-pressure headroom scales with physical RAM: one eighth of RAM,
  constrained to 512–2,048 MiB, using MemAvailable rather than MemFree.
- Files thumbnail caches have a 32 MiB estimated decoded RGBA byte budget in
  addition to their entry limit. The launcher cache has a 16 MiB budget. Active
  leases are separately counted; these are cache budgets, not hard limits on
  images currently displayed by applications.
- Cache invalidation cannot be undone by a decode that was already in flight.
  Images too large to cache remain valid until their consumer releases them.
- Every 15 seconds the Linux shell checks pressure. Under pressure it releases
  hidden launcher icons, a closed App Store session with no active package
  operation, unused thumbnails, and unleased wallpaper sources. It does not
  force GC or invalidate images still being rendered. The launcher reloads icons
  on its next opening and retains warm icons normally when pressure is absent.
- Trash uses one nonrecursive directory watcher, coalesces events, and checks
  directory contents off the UI thread. A 30-second recovery check handles
  missing directories, overflow, and lost events. It replaces the former
  once-per-second UI-thread filesystem enumeration.
- Launcher icon workers use an appearance snapshot captured on the UI thread,
  avoiding a synchronous round trip back to that thread during icon resolution.
- Newly started native development sessions default to Release. The active
  supervisor records its configuration, and the restart controller builds that
  same configuration. Older live Debug supervisors continue to get Debug builds.
  The installer and repair path use the selected configuration too. Existing
  binaries remain the fast login path, without rebuilding on every source edit.

## Real resource counters

Optional JSONL resource tracing records RSS, PSS, private/anonymous resident bytes,
managed estimated live bytes, managed heap and fragmentation at the last GC,
committed managed bytes, total allocated bytes, collection counts, cumulative CPU
milliseconds, and wallpaper/thumbnail cache ownership. It does not distribute
CPU or RAM among controls using visual-tree weights.

Enable before starting the shell using an existing output directory:

```bash
export DAISYOS_RESOURCE_TRACE=/tmp/daisy-shell-resources.jsonl
```

For an already installed supervisor whose environment cannot be changed, create
`$XDG_RUNTIME_DIR/daisyos/resource-trace.enabled` before a controlled shell restart.
Counters then go to `$XDG_RUNTIME_DIR/daisyos/resource-trace.jsonl`. Removing the
marker stops this trace at its next sample. Do not leave the marker enabled for
normal sessions. Both modes sample every five seconds and stop at 4 MiB of output.

Counter scopes matter: committed managed memory is not live object size; PSS
includes native/driver memory; dedicated GPU memory requires separate driver
sampling. A cold restart is not a controlled before/after comparison against a
long-running shell.

## Validation

Validation results and live-session observations are recorded below as checks
complete. The installed supervisor is not replaced by editing repository scripts;
the Release default requires installing the updated launcher and a new login.

## Additional live finding: desktop-services restart loop

The session check found the desktop-services unit repeatedly exiting because a
KDE PolicyKit agent was already registered. Its journal recorded about 1.5–1.6
CPU-seconds per failed process, approximately 3.2–3.4 seconds alive, followed by
systemd's restart delay. This was avoidable background work and also left the
public portal with zero capture sources.

Portal startup now succeeds independently of the authentication helper. A helper
failure or exit cannot tear down file picking and capture. Only that small native
helper retries, at 30-second intervals, and can recover if the competing agent
later exits. The existing registered authentication agent was left running.
The desktop-services launcher also follows the active supervisor's configuration.

The already installed development service loaded the rebuilt Debug binary on its
next automatic attempt. It then stayed active at PID 383243; NRestarts remained
44 throughout the final 60-second sample. Its measured CPU averaged 0.23% of one
core. After refreshing the portal frontend, both backend and frontend advertised
AvailableSourceTypes=3. This verifies service availability, not permission-approved
capture frames or a complete new authentication flow.

## Completed checks and current limits

- Seven thumbnail-cache/visual-loading tests passed, including byte eviction,
  oversized image leases, invalidation during decode, and pressure cleanup.
- Seven prefetch/memory-headroom tests passed.
- The final 25-test headless group passed: launcher lifecycle, wallpaper source
  ownership, Trash events, shell motion, and portal pickers. Early combined runs
  had icon timeouts; the new launcher test now uses its own image and state
  fixture and explicitly releases its resources at teardown.
- Native-session installer tests passed, including failed-build/install safety,
  legacy Debug restart behavior, and recorded Release restart behavior.
- Supervisor recovery tests passed for normal exit, crash, TERM, KILL, bounded
  backoff, and healthy-uptime reset.
- Shell script syntax checks passed. ShellCheck passed with the two existing
  SC2155 cursor-export warnings excluded. `git diff --check` passed.
- The rebuilt Debug shell's compiled-resource verification passed. A controlled
  reload replaced shell PID 324128 with 381001 while supervisor 947 and KWin 1103
  remained alive. The live launcher screenshot was inspected.
- The final compositor-session report has zero required failures and warnings.
- Temporary live resource tracing was disabled after collecting the sample.

The final live sample averaged 0.99% of one core for the shell. Its last counters
were 473.9 MiB PSS, 716.4 MiB RSS, 361.9 MiB private resident, 100.3 MiB committed
managed memory, and approximately 4.3 MiB of live launcher thumbnails. Those
figures come from a freshly restarted Debug shell with the IDE still open;
compilation was also occurring during sampling. They are not a controlled idle
Release benchmark or proof of a persistent 50% memory reduction against the
long-running original process. Native/graphics allocations remain a major part
of the footprint. No RDR2 performance or OOM acceptance claim is made.

Raw validation outputs and screenshots are under
`artifacts/performance/2026-10-22-optimizations/`. The resource budgets in the audit
remain unachieved acceptance goals, not guarantees established by these changes.

The optimized Release shell build also passed (zero errors). Existing LocalSend
trim/AOT and platform analyzer warnings remain. Release compiled-resource verification passed, and Files, Notes, Settings, and
Disk Manager all rebuilt successfully in Release with zero warnings/errors. Their
copied shell assemblies now include the shared thumbnail-cache changes.
