# DaisyLaunch

DaisyLaunch implements the production KIO-first launch pipeline from the application launch blueprint. It is a C++20 Qt 6 / KDE Frameworks 6 session service, with a persistent asynchronous C# D-Bus client. The application grid, search and external taskbar launches use it. Packaged DaisyOS apps retain their existing single-instance pipes and development-binary selection; their process startup now runs off the UI thread.

## Build and install

```bash
scripts/install-daisy-launch.sh
systemctl --user status daisy-launch.service
```

Requires Qt6 Core/Gui/DBus/Concurrent and KF6 CoreAddons/Service/KIO development packages, CMake and Ninja. Installation is per user and D-Bus activatable, starts with `graphical-session.target`, and restarts after crashes. The user unit lives in `session.slice`. KIO handles app.slice/transient-unit integration where the session user manager supports it. `KillMode=process` and KIO's independent app ownership prevent a service restart from killing launched apps. Qt automatic quit locking is disabled so completion of a launch job does not stop the persistent service.

## Registry and routing

KService/sycoca provides desktop-ID precedence, hidden and desktop-specific filtering, terminal metadata, field codes and actions. A read-only shared snapshot contains desktop IDs, KService metadata, localized label maps and categories. Catalog JSON is serialized during discovery, never during a click. Recursive application-directory watches asynchronously refresh sycoca even without kded; sycoca changes trigger a debounced worker rebuild and atomic snapshot replacement; failed rebuilds retain the old snapshot. The shell invalidates its cached catalog and lookup dictionary when the service publishes a new registry or changes ownership. Localized labels are selected using the shell's current UI culture. KService's sycoca is the maintained disk cache; DaisyOS does not introduce a second desktop parser cache.

Ordinary installed desktop entries use `KIO::ApplicationLauncherJob`. `DBusActivatable` entries use asynchronous `org.freedesktop.Application.Activate`, `Open`, or `ActivateAction` with activation platform data; these never silently execute `Exec` on activation failure. URI delivery and actions bypass accidental-click suppression. Desktop shortcut files use GIO's maintained desktop launcher on a worker. Arbitrary executable names are no longer inferred from unrecognized taskbar IDs.

The service accepts only resolved desktop IDs, declared actions and bounded URI lists, never arbitrary commands. Session-bus caller UID must match the service user; request results, cancellation and traces belong to their originating connection. Limits are 64 URIs (8192 characters each), 16 pending requests per caller, 20 requests per second per caller and 1024 retained requests globally. Unknown options are rejected. Request IDs are unique 32-character ASCII hexadecimal strings; repeated identical IDs are idempotent and conflicting payloads are rejected.

`Launch` means queued, not visible. `Await` asynchronously replies when the backend confirms dispatch. Backend operations time out after 15 seconds without killing applications. Requests and traces expire after two minutes. Cancellation stops observation, not an already dispatched app. Errors include NotFound, InvalidArgument, RateLimited, BackendFailed, TimedOut and Cancelled. Ambiguous connection loss is reported as Unknown and never automatically retried: the app may already be running. A proven service-activation failure uses the one-shot KIO fallback (`daisy-launch --launch desktop-id`).

## Activation and feedback

The Avalonia Wayland fork propagates seat, serial and originating surface cookies for actual pointer, touch and keyboard input. `WaylandActivation.RequestAsync` requests a single-use xdg-activation token on the Wayland worker; the app grid/search and taskbar capture it before dismissing their surface. Token acquisition has a 300 ms observation deadline and does not block the UI thread. Compositor refusal or absence produces an empty token. Tokens are passed as KIO startup IDs or D-Bus `activation-token` platform data. Programmatic/background launches do not fabricate input tokens.

Existing shell focus/restore policy remains compositor-owned. Launch feedback uses the existing taskbar indicator, clears on known failures and new matching compositor windows, and expires after 15 seconds for windowless applications. It does not pretend a window is usable or physically presented. Correlation compares stable desktop/app IDs and excludes windows that existed before the request. No screenshot splash, window scan on the click path, or application window recreation is introduced.

## Flags and rollback

| Environment variable | Default | Meaning |
| --- | --- | --- |
| `DAISYOS_LAUNCH_SERVICE` | `1` | Set `0` to use the supported one-shot KIO fallback |
| `DAISYOS_LAUNCH_DEDUPE` | `1` | Set `0` to disable the 200 ms duplicate gate for default empty-URI launches |
| `DAISYOS_LAUNCH_FEEDBACK` | `1` | Set `0` to disable taskbar launch indication |
| `DAISYOS_LAUNCH_TELEMETRY` | `1` | Set `0` in the service environment to disable bounded local stage traces |

No upload, custom spawn backend, priority boost, third-party speculative execution or freeze/thaw backend is enabled. Those blueprint phases are conditional on demonstrated benefits and resource budgets, not assumed improvements. Existing executable-page prefetch is separate from launching/prestarting applications.

## Verification and benchmarks

```bash
python3 scripts/test-daisy-launch.py
python3 scripts/test-os-performance.py
python3 scripts/benchmark-launch-dispatch.py --output artifacts/performance/launch-dispatch-next.json
python3 scripts/benchmark-os-performance.py run --build --output artifacts/performance/launch-optimization-next.json
python3 scripts/benchmark-os-performance.py compare artifacts/performance/launch-optimization-verified.json artifacts/performance/launch-optimization-next.json
```

The integration test uses a private bus and disposable XDG entries, testing real KIO dispatch/actions, D-Bus Activate/Open/ActivateAction, cancellation, malformed requests, caller isolation, registry updates and application survival. It does not touch user applications or their profiles.

The dispatch benchmark uses the real C# client and unchanged original `SafeProcessLauncher` direct-Exec implementation, 30 samples and three discarded warmups per route, seeded interleaved service/fallback/direct ordering, raw monotonic stage timestamps, p99, paired bootstrap confidence intervals and daemon CPU/PSS/RSS. The fixture is a windowless `/bin/true` desktop entry. It includes both the supported one-shot KIO fallback and the original direct-Exec backend. The private bus has no systemd user manager; it exercises KIO's process fallback. These results measure amortized Qt/KIO dispatch overhead, not a speed gain in app initialization. The existing OS suite continues to launch fresh app binaries directly, so it measures app initialization independently of the new launcher pipeline and preserves the original scoring protocol.

Unavailable first-commit, presented-frame and app-ready events are null, never zero. KWin task-management snapshots identify windows but cannot measure scanout. Physical focus/token acceptance, shell frame pacing under contention, terminal/Flatpak/Electron launches, multi-monitor/multi-seat behavior, crash-after-dispatch reporting through systemd, hardware-class comparisons and an instrumented presentation test remain release acceptance gates. They are not established by a build or this windowless benchmark. App initialization/compositor-internal time is classified separately until app-level profiling explains it; no claim that 95% of startup has been attributed is made.

API sources: [KIO ApplicationLauncherJob](https://api.kde.org/kio-applicationlauncherjob.html), [desktop D-Bus activation](https://specifications.freedesktop.org/desktop-entry/latest/dbus.html), and [desktop entry semantics](https://specifications.freedesktop.org/desktop-entry/latest-single/).

Reference hardware and environment are pinned in [launch-performance-reference.md](launch-performance-reference.md). Local tracing stores fixed timestamp arrays in bounded request records and serializes them only when queried; it never writes on the dispatch path.

The completed run is logged in [app startup times.md](../app%20startup%20times.md). Dispatch medians were 2.28 ms for the service, 21.39 ms for the original direct path, and 159.60 ms for the supported one-shot KIO fallback. The paired median service saving versus the original path was 19.08 ms (95% bootstrap interval 16.37–21.14 ms). The original helper resolves XWayland by scanning `/proc` during each launch; the new pipeline consumes the session environment without that scan. These windowless fixture numbers do not prove GUI readiness or presented-frame gains.
