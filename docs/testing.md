# Testing

## Static Checks

```bash
scripts/format-check.sh
```

This checks C# formatting with `dotnet format --verify-no-changes` and shell scripts with ShellCheck when the required tools are installed.

## Build Check

```bash
scripts/test.sh
```

The script restores and builds the solution, then runs the service/unit suite
in `tests/DaisyOS.Tests` and the isolated Avalonia rendering suite in
`tests/DaisyOS.ShellDemo.Tests`.

Current automated coverage includes:

- Safe command output, errors, exit codes, literal arguments, missing executables, failure logging, timeouts, and caller cancellation.
- JSON settings defaults, malformed-file recovery, persistence, normalization, and change notifications.
- Guarded asynchronous command overlap and error containment.
- Desktop-entry command parsing and malformed command rejection.
- Read-only pacman update-line parsing.
- File-manager filtering, hidden-file visibility, breadcrumbs, history navigation, and persistent location pins.
- Wi-Fi and Bluetooth parsing and truthful failure behavior.
- App-policy states and internal/external decisions.
- Idle-lock timing, startup options, density selection, media artwork loading, and wallpaper layout calculations.
- Release argument validation, artifact naming, precedence, and checksum generation.
- Gaming diagnostics and calm missing-capability behavior.
- Onboarding navigation, completion persistence, exit rules, and service-failure wording.
- KWin session preflight classification for both healthy and broken fixture environments.
- Shell loading, empty, offline, unavailable, and error-binding contracts.
- Notification visibility policy, including error/critical bypass of quiet modes.
- Calm file, desktop, launcher, power, and network failure wording with no raw exception leakage.
- Headless rendering and interaction coverage for the polished shell demo,
  including overlay exclusivity, Escape behavior, lock-screen shortcut
  suppression, supported shell resolutions, and minimum Settings/Files sizes.

Run only the automated tests with:

```bash
dotnet test
```

Tests that write settings use unique temporary directories and do not touch the user's DaisyOS configuration.

## Polished shell demonstration

Run the repeatable shell-surface gate and export its evidence frames with:

```bash
scripts/capture-shell-demo.sh
```

The acceptance contract, interactive sequence, supported sizes, and explicit
non-goals are documented in `docs/shell-demo.md`.

## Compositor session validation

Repository checks cannot prove that a real Wayland session works. Use the matrix
in `docs/compositor-test-matrix.md` for nested KWin and disposable-QEMU runs.
The preflight command is read-only:

```bash
scripts/check-compositor-session.sh \
  --output artifacts/compositor/session-report.md
```

Add `--strict` only inside a KWin test session. Strict mode returns a failure
when Wayland, KWin, XWayland, or the DaisyOS shell is missing. Portal and
PipeWire checks remain warnings so the report still explains a partially
working session without changing it.

From an existing graphical session, the automated nested runtime gate is:

```bash
scripts/test-kwin-nested-runtime.sh
```

It starts an isolated windowed KWin instance, verifies a real Wayland client
round trip, launches the production shell and an XWayland test client, runs the
strict preflight against the exact nested KWin PID, closes the nested process
tree, and confirms the parent graphical session still responds. Evidence is
written to `artifacts/compositor/nested-runtime/`. It does not replace the
matrix's interactive focus, clipboard, decorations, scaling, or visual checks.

Set `DAISYOS_CAPTURE_NESTED_SCREENSHOT=1` to also request a best-effort
compositor screenshot. Screenshot support is not a hard gate because isolated
nested compositors do not always expose a compatible capture interface.

## Resource regression capture

From an existing graphical session, capture a one-shot real-service snapshot:

```bash
DAISYOS_RESOURCE_REPORT_DELAY_SECONDS=30 \
  scripts/capture-shell-resources.sh artifacts/diagnostics/shell-resources.txt
```

The exporter remains inactive during normal launches. It samples one-second CPU
intervals and writes memory, GC, backdrop, thread, handle, and visual-tree counts
after the requested settling delay. Compare results only when build type,
display configuration, open windows, wallpaper, and service mode match.

## Stabilization Baseline

The 2026-07-10 stabilization pass recorded these local Linux x64 measurements:

- Bundled shell assets: 35 MB before and 12 MB after lossless/visually equivalent resizing and recompression.
- Self-contained Release publish: 117 MB (121,872,624 bytes) after optimization; trimming remains disabled for Avalonia compatibility.
- Wayland-session mock smoke test: the published process remained responsive for 25 seconds with a clean log, settled to roughly 2% CPU over a five-second idle sample, and used about 610 MiB RSS on the development machine.
- Automated tests: 105 passing xUnit tests plus release-script checks at the start of the release-pipeline pass.

These figures are machine-specific and are intended for regression comparison, not as universal hardware requirements. Re-run the same Release artifact and sampling interval when comparing future changes.

## Manual Shell Smoke Test

The release-level automated gate for the non-installing live environment is:

```bash
scripts/validate-live-iso-alpha.sh out/DaisyOS-YYYY.MM.DD-x86_64.iso
```

Do not mark the bootable live-ISO alpha complete from a manual checklist or an
older ISO. The acceptance report must match the current source fingerprint and
embedded production shell payload.

After installing .NET SDK 10:

- Run `dotnet run --project src/DaisyOS.Shell`.
- Confirm the shell opens fullscreen.
- Confirm the clock updates.
- Open Launcher from the dock.
- Open Settings from the dock.
- Open Power from the dock.
- Open Notifications from the system bar.
- Open Updates and verify both available-package and up-to-date mock states.
- Open Files and confirm sidebar labels are left aligned, previews cancel when selection changes, and destructive actions still require confirmation.
- Minimize and restore Settings and Files repeatedly; confirm dock state stays synchronized.
- Press `Escape` to close overlays.
- Press `Ctrl+Q` to quit.

## Manual File Manager Safety Check

- Move a disposable file and folder to Trash.
- Choose **Delete permanently**, then cancel and confirm the item remains.
- Repeat and confirm deletion; verify the item and its Trash metadata are removed.
- Choose **Empty Trash**, cancel, and verify all remaining items remain.
- Confirm **Empty Trash** and verify the Trash count returns to zero.
