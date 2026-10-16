# Global UX and release audit — 2026-10-16

This pass improves specific loading and application-management paths. It does not certify the whole operating system, every animation, or every application as removable. No installable production ISO was produced or accepted in this pass.

## Implemented

- App Store discovery derives featured cards from the already loaded catalog results. It no longer runs a second installed-app scan before publishing discovery. Filtered results are materialized once per change rather than repeatedly for result-count and pagination bindings.
- Refreshing the current App Store page keeps its existing cards usable, including when refresh fails. Changing pages clears the previous page and shows skeleton placeholders. A thin progress indicator distinguishes background refresh from initial loading. Existing request-version and cancellation guards protect against stale search publication.
- Installed-app listing uses local catalog metadata only, falls back to actual installed IDs/names if metadata is unreadable, and does not wait behind a catalog download. Native desktop-file names are indexed once per scan instead of issuing existence checks for every catalog application.
- Settings shows its installed-app skeleton immediately, enumerates applications on a worker, and creates rows in batches of eight with dispatcher yields. Navigation-version checks discard work for abandoned pages.
- Files shows skeletons while a directory is loading without projected entries. Existing entries remain visible when available. Empty-state and skeleton notifications follow loading and projection changes.
- Calculator mode changes publish immediately and animate the new header. The fixed 120 ms delay and disabled mode button were removed.
- Game artwork requests return a placeholder or an in-memory result immediately. Disk metadata reads and synchronous provider/image validation run on workers with four concurrent resolutions and one pending resolution per game. Completed artwork is published through the existing UI-dispatched update event.
- Native applications recognized by App Store can request removal of their actual Pacman package. Ownership uses machine-readable `pacman -Qqo`; removal retains dependency checks and requests normal administrator authentication. Unsupported/unowned applications report failure rather than pretending to uninstall.
- Launcher and Settings Flatpak operations retain the resolved user, system, or named installation. Repair requests a reinstall from the original remote.
- App Store, Launcher and Settings share one content-sized removal dialog with a safe default/cancel action. This removes duplicated fixed-height dialog implementations.
- Reveal motion can be attached to visibility changes without accumulating repeated subscriptions. Existing shared motion handles cancellation and reduced motion.
- All remaining literal hexadecimal XAML colors outside DaisyTheme were moved to shared resources: Notes document palettes, artwork controls/gradients, desktop item shadows, widget previews, and screenshot selection. Document palette values remain fixed document data rather than changing with desktop theme.

## Release blockers and remaining scope

| Priority | Finding | Evidence and required completion |
| --- | --- | --- |
| Release blocker | Disk installation is a simulation | `src/DaisyOS.Installer/App.axaml.cs` instantiates `MockInstallerBackend`; `MockInstallerBackend.CanModifyDisks` is false. A real backend needs a reviewed storage/boot/account/recovery implementation and disposable-VM installation/reboot acceptance before physical disks. |
| Release blocker | Bundled applications cannot all be removed independently | `InstalledApplicationService.ResolveAsync` treats internal applications as system-managed. `LinuxAppLauncherService` supplies built-in applications, and the Arch package is `daisyos-shell`. Independently removable built-ins need separate package/registration ownership and launch availability rules. Hiding an entry would not meet removal acceptance. |
| Release blocker | Current media is a live alpha | README and `docs/iso-build.md` explicitly describe a live preview. Source/profile checks cannot certify a newly built image, installed upgrades, rollback, or recovery. |
| Required acceptance | Real gaming and multitasking | Session services and packaging pass focused checks. Real Steam/Proton games, frame-time traces, fullscreen layering, alt-tab, multiple windows, controller handling, and installed driver variants still need interactive runs. No before/after game FPS claim is made. |
| Further UX work | Every transition and every page | This pass covers the listed loading/interaction paths. Console overlay fixed-delay dismissal, lock-screen dismissal timing, startup protocol waits and other pages remain separate review items; service/debounce waits must not be removed merely because they are delays. |
| Further distribution work | Broad installation sources and package failures | App Store uses Flatpak plus native package removal for recognized applications. Arbitrary AppImages, local package installers, unknown desktop shortcuts and independent bundled-app packages are not covered by a universal install/remove UI. |

## Validation

- Shell build completed successfully before the final integration runs. Restricted MSBuild task hosting required running the Avalonia build outside the filesystem sandbox.
- App Store UI suite: 38 passed, including retained refresh results, skeleton/page separation, rendered skeleton bounds, and Flatpak scope/remote handling.
- Settings layout, shared motion/reversal/reduced-motion, and Files title integration: 17 passed.
- Files navigation/cancellation integration: 3 passed, including skeleton publication when leaving a populated directory. Across these four focused suites: 90 tests passed.
- Skeleton renders at 800×540 in Light and Dark were inspected. Evidence: `artifacts/global-ux-2026-10-16/daisy-store-loading-Light.png` and `daisy-store-loading-Dark.png`.
- App Store service, gaming artwork, cache and image-validation integration: 32 passed. The slow-disk test blocks cache access while verifying that requests still return placeholders and coalesce into one resolution.
- Theme-token check, ISO profile check, gaming integration fixtures (3), session supervisor tests, release-script tests and system-app-shortcut migration test passed.
- Read-only native session check: 10 passed, zero warnings. Evidence: `artifacts/global-ux-2026-10-16/session-health.md`.
- Arch packaging checks, production publish-contract checks, and compositor-session checker fixtures passed.
- Global localization verification failed on the existing literal `FPS` label in `Views/Components/Gaming/GamingOverlay.axaml`. This view is unchanged by this patch; the full repository gate is not green. A separate scan of changed Shell C# files also found existing uncatalogued `GAMING` and gaming-description strings in Settings.
- Existing LocalSend certificate/trimming/platform analyzer warnings are outside this patch. No package was installed or removed on the host, no disk was formatted, and no shell/session restart or new ISO deployment was performed for this validation.

No measured launch-latency or frame-rate improvement is claimed. The regressions verify concrete behavior and removal of specific waits/work; physical presentation and hardware acceptance remain distinct.
