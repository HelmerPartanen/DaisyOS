# DaisyOS cleanup review — 2026-10-04

## Scope and removal evidence

Reviewed the project instructions, solution/project references, shell startup and
service ownership, Core/System types, shell helper usage, serialization contexts,
XAML references, native integrations, session/release scripts, and packaging.
The initial root worktree was clean. Three existing Avalonia Wayland edits were
preserved: `WSurface.cs`, `WaylandWorkerClient.cs`, and `WindowImpl.Sink.cs`.

Removal candidates were checked against source, tests, XAML, configuration,
native code, build scripts, and explicit reflection sites. Constructors and
declarations were not counted as consumers. Wildcard linker roots preserve
assemblies but do not instantiate otherwise unused services. Historical roadmap
mentions and old staged binaries are not current source consumers; staged ISO
payloads were left intact and have not been regenerated.

## Completed passes

- Core/System: removed the unimplemented settings/theme interfaces and the
  disconnected storage-status and generic capability-probing services, contracts,
  and models. Settings still reads storage through its existing `DriveInfo` path;
  Files still uses `VolumeService`. Live service implementations remain intact.
- Shell: removed unused `ItemMotionAnimator`, `BitmapAssetValueConverter`, and
  `AppGroupViewModel`. Desktop animation still belongs to `DesktopReorderPanel`.
  Removed unused synchronous decoration-export/contrast helpers, an obsolete
  wallpaper direct-source predicate, five unused JSON option fields, and the
  unused Notes title field. Source-generated JSON settings remain unchanged.
- Dependencies: removed `MaterialColorUtilities`; no namespace, type, resource,
  reflection, or build consumers were found. Its runtime dependency entry is gone.
  SkiaSharp, SVG, D-Bus, the pinned Avalonia fork, and ASP.NET remain in use.
- Packaging: restored the missing windowed-development session desktop entry and
  made that source file visible to Git. Recovery/session behavior is preserved.
- Validation: removed the empty Core template test. Diagnosed a pre-existing
  shell-test hang caused by theme lookup dispatching without an Application;
  return the existing dark default before dispatching in that case, with a bounded
  worker-thread regression test. Running-application theme lookup is unchanged.

## Validation

- Core/System removal pass: 342 service/contract tests passed; one opt-in live
  screenshot test skipped.
- Final cleanup before the dispatcher guard: 342 service/contract tests and 31
  headless interaction/render tests passed. Shell compilation and compiled XAML
  resource loading passed. The shell suite reproduced its baseline hang.
- Core suite after removing the empty template test: three tests passed.
- Shell suite after the application-free dispatcher guard: 79 tests passed,
  including the new worker-thread regression test; the suite completed in one
  second rather than hanging. Together the focused suites cover 455 passing tests
  and one opt-in screenshot skip.
- Release, ISO worktree guard, publish contract, KWin prototype, packaging,
  packaging-check fixtures, branding, session supervisor, native-development
  installer, compositor-check fixtures, and ISO-profile checks passed.
- Restored windowed-development installer: staging and desktop-file validation
  passed. Branding's optional sound-manifest check was skipped because `jq`
  is unavailable.
- `git diff --check` passed.
- The full repository gate encountered the existing Avalonia designer prerequisite
  failure: `dnx Bun.Unofficial.Tool --yes -- install` cannot run because `dnx` is
  missing. A broader run also reported legacy net8 assembly file contention
  during overlapping validation builds. The bounded `scripts/test.sh` run did not complete. Formatting reports
  extensive existing whitespace/import errors; ShellCheck reports existing issues
  in native build and blur-patch test scripts. These gates are not claimed green.

## Runtime evidence and limits

The cleaned Debug shell started in a separate D-Bus/nested KWin session using
native layer-shell, stayed alive for 20 seconds, and the session exited with code
zero. A repeat after the dispatcher guard aborted with `free(): invalid pointer`.
The system core report identifies `libharfbuzz.so.0` and bundled
`libHarfBuzzSharp.so` in the failing font-creation stack. Native startup acceptance
is therefore unresolved, despite the first successful run. The parent Wayland
display responded after both runs. Evidence is under
`artifacts/cleanup-2026-10-04/`.

A separate copy of the pre-cleanup source was rebuilt successfully, retaining the
same existing Avalonia edits and native wallpaper library. It reproduced the
abort, and its core report has the same system-HarfBuzz/bundled-HarfBuzzSharp
font-creation stack. This establishes a pre-existing native startup blocker;
it does not establish a complete root cause or a fix. Both baseline and cleaned
startup evidence are retained. The main service/contract and headless suites were
rebuilt and passed again after the dispatcher guard.

LocalSend safely reported port 53317 already in use by the main desktop. This
smoke test does not verify transfers, physical input, appearance, scaling,
clipboard, PAM authentication, hardware output, or ISO boot. The main desktop
was not restarted to deploy the cleanup.

## Complexity retained

Keep MVVM/binding-facing properties and constructors, source-generated JSON
contexts, explicit platform service boundaries, the Avalonia fork and native ABI
glue, KWin bridge/surface ownership, wallpaper resource lifetimes, Files
reconciliation/cancellation/virtualization, and session recovery paths. Their
complexity supports existing behavior or reliability. Xorg/XWayland recovery and
mock-only installer safety remain intentional compatibility boundaries.

Other low-reference methods, public service interfaces, repeated UI construction,
and tracked native/staged build artifacts were not removed solely on low reference
counts or resemblance. Broader consolidation needs individual behavior and
runtime evidence. This review does not assert that every remaining line is
minimal or that all desktop acceptance gates are complete.
