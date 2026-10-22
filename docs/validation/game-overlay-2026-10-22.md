# Steam FPS integration and RAM scope — 2026-10-22

The user reports much better RDR2 frame pacing after the resource changes, but
no FPS reading and approximately 14 GiB in the hardware overlay. This is useful
live acceptance feedback, not a matched benchmark.

## Findings

- The active development session and shell had no MangoHud environment.
- Native Steam's desktop entry still executed `/usr/bin/steam` directly. The
  development app installer copied the game-launcher helpers but never installed
  a user-level Steam desktop override. Normally opening Steam therefore bypassed
  instrumentation, even though image staging includes wrapped entries.
- The installed local MangoHud runtime itself works: a native Wayland Vulkan
  probe created its real per-process control socket, accepted hidden logging,
  and produced approximately 180 FPS in its CSV.
- Overlay RAM is total physical RAM minus MemAvailable. It includes the game,
  Steam, desktop, kernel allocations, and other applications. It is not a
  measurement of the game alone. Reclaimable cache is already reflected in
  MemAvailable; switching to MemFree would inflate the apparent usage further.

At the initial observation RDR2/Proton and Steam were not running. Approximately
4,130 MiB PSS belonged to editor/development tooling; the main shell accounted
for about 427 MiB PSS, with another shell-binary process used for desktop services.
MemTotal minus MemAvailable was about 5.4 GiB. Some process smaps were inaccessible,
so process sums are not an exact accounting of the entire machine. These numbers
cannot reconstruct the earlier 14 GiB gaming workload or establish RDR2's own RAM.
No game-only RAM amount is inferred by subtraction from a different workload.

## Changes

`install-user-gaming-integration.py` installs user-level telemetry helpers and
wraps native Steam/Lutris desktop Exec lines. Localized names, command arguments,
and desktop actions are preserved. DBus activation is disabled so the launcher
runs. Existing user entries get a one-time content backup; repeated installation
is idempotent. `install-system-apps.sh` now invokes this installer, covering future
development installs as well as image staging's existing integration.

The tested Steam override was installed for this user and desktop caches were
refreshed. Steam was already closed. A previously running Steam must fully exit
and reopen to inherit new environment variables. Per-game launch options and
Steam userdata are unchanged. Explicitly running `/usr/bin/steam` still bypasses
the user desktop wrapper; this change covers normal desktop/menu launches.

The overlay now labels its memory field `SYS RAM` and uses `GiB` for the binary
unit it already calculated. The updated Debug shell was built, its compiled
resources verified, and it was reloaded under the existing supervisor without
restarting KWin.

## Verification

- Four installer tests pass, including preservation of custom arguments and
  localized names/actions, DBus override placement, quoted paths, backup behavior,
  and idempotent installation.
- Three automatic gaming integration tests and two launcher/control-socket
  tests pass. Native socket tests require an execution context permitting Unix
  sockets; sandbox execution denied socket creation, and the unrestricted run passed.
- Thirteen .NET FPS telemetry tests pass, covering control activation/deactivation,
  retry, directory discovery, CSV parsing, and bounded log tails.
- Desktop entry validation found no errors. It retained the distribution entry's
  existing deprecated KDE-key warning and multiple-category hint.
- A live `vkcube` was launched through the installed automatic wrapper, with no
  manual logging command or per-game option. Opening the shell overlay caused
  capture to start; its CSV recorded 179.926 FPS, and the inspected screenshot
  shows **180 FPS** and **SYS RAM 5.4 GiB**.
- The probe process was terminated and the overlay's original visibility restored.
  `git diff --check` and the changed shell script's ShellCheck pass.

Artifacts are in `artifacts/performance/2026-10-22-game-overlay/`.
This verifies the native launcher-to-overlay pipeline. RDR2/Proton container
compatibility and the actual per-process breakdown at the earlier 14 GiB reading
remain unverified until an in-game sample is available. No memory reduction or
new RDR2 benchmark gain is claimed by this overlay/integration change.
