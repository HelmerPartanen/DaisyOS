# Continuity repository integration

Audit date: 2026-10-28. This is an implementation record, not a feature announcement.
The supplied specification is preserved in [MASTER_PLAN.md](MASTER_PLAN.md).

## Actual architecture

DaisyOS.sln contains C# projects targeting .NET 10. `global.json` selects SDK
10.0.100 with latestFeature roll-forward. The available SDK is 10.0.113, runtime
10.0.13, Linux arch-x64. `Directory.Build.props` configures workstation GC and
memory conservation. There is no DaisyOS Rust workspace or Smithay compositor.

The shell uses a pinned source fork under `external/Avalonia`, including
`external/Avalonia/src/Avalonia.Wayland`. Do not mix its assemblies with upstream
NuGet Avalonia. `src/DaisyOS.Shell/Program.cs` initializes the shell;
`scripts/daisyos-native-development-session` supervises KWin and the native shell;
`scripts/daisyos-development-session` runs the windowed development session.
KWin owns global input, focus, output topology and Wayland security.

| Concern | Existing integration point | Continuity integration |
| --- | --- | --- |
| Settings | `src/DaisyOS.Settings/Program.cs`, `src/DaisyOS.Shell/Apps/System/Settings/SettingsView.axaml` and `.axaml.cs` | Extend existing category navigation and resources; standalone Settings must use daemon IPC |
| Files | `src/DaisyOS.Files/Program.cs`, `src/DaisyOS.Shell/Apps/Files/FilesWindow.axaml.cs`, `FilesViewModel.cs` | Add a separate trusted-device send action beside existing LocalSend action |
| Notifications | `src/DaisyOS.Shell/Services/Notifications/NotificationService.cs`, `FreedesktopNotificationDaemonService.cs` | Use desktop notifications; daemon must survive absent shell UI |
| Session IPC | `src/DaisyOS.System/Processes/DaisyLaunchClient.cs`, `services/DaisyLaunch/main.cpp` | Follow versioned D-Bus naming, asynchronous calls and owner-change recovery |
| Portal | `src/DaisyOS.Shell/Services/DesktopIntegration/DesktopServicesApplication.cs`, `NotificationPortal.cs` | Existing portal process is separate; capability availability must be probed |
| Lock | `src/DaisyOS.System/Security/LinuxLockScreenService.cs`, `src/DaisyOS.Shell/App.axaml.cs` | In-memory LockStateChanged alone is insufficient for a separate daemon; combine authoritative logind/session state with shell lock state, fail closed on owner loss |
| Session information | `src/DaisyOS.System/Session/LinuxUserSessionService.cs` | Environment snapshot is not an authoritative lock/logout subscription |
| Clipboard | `external/Avalonia/src/Avalonia.Wayland/Clipboard/WaylandClipboardImpl.cs`, `Server/Transient/Clipboard/WaylandDataDevice.cs` | Normal application selection implementation is not proof of background clipboard-manager authorization |
| Clipboard history | `src/DaisyOS.Core/Services/ClipboardHistoryService.cs` | Local history is deliberately outside Continuity; never sync it |
| Logging | `src/DaisyOS.Core/Services/ILogService.cs`, `NullLogService.cs` | Payload-free stable event codes; daemon journald metadata |
| Lifecycle | `packaging/arch/daisyos-shell.service`, `packaging/xdg-desktop-portal/daisyos-desktop-services.service` | New independent user service, tied to active graphical session and explicit opt-in |
| Application launch | `src/DaisyOS.System/Processes/LinuxAppLauncherService.cs`, `DaisyLaunchClient.cs` | Future handoff validates registry application IDs; never execute peer commands |
| Tests | `tests/DaisyOS.Core.Tests`, `tests/DaisyOS.Tests`, `tests/DaisyOS.Shell.Tests`, `scripts/test.sh` | Pure policy tests first; integration and two-machine gates separate |

## Existing networking is not a Continuity trust boundary

`src/DaisyOS.System/Networking/LocalSend/LocalSendService.cs` implements an
embedded Kestrel HTTPS LocalSend v2 server on 53317, with multicast discovery,
network-change handling and consent/progress events. It is shell-owned
(`src/DaisyOS.Shell/App.axaml.cs`), and not an independent trusted-peer service.
Its HTTPS listener allows client certificates with a permissive validation
callback. That LocalSend compatibility behavior cannot authenticate Continuity
peers. Do not copy its verifier, discovery identity, or approvals into Continuity.
Keep the existing LocalSend UX and protocol working independently.

## Implementation decision

Use C# for shared policy models and thin Settings/Files adapters. Keep these in
`src/DaisyOS.Core/Continuity` with no platform dependencies or activation hooks.
No existing general-purpose authenticated peer daemon was found. The proposed
Rust Tokio/Quinn daemon remains a suitable separate networking process, with
Avahi system D-Bus and session D-Bus UI integration. Do not add Rust to the shell
render process. Select/pin the complete Rust dependency graph and generated
bindings together when the daemon is implemented; this foundation introduces no
new packages and makes no assertion that pairing has been cryptographically
reviewed. A reviewed pairing design is a release blocker, not an invitation to
invent a TLS verifier or comparison algorithm.

Proposed (not existing) IPC: `org.daisyos.Continuity1`, object path
`/org/daisyos/Continuity1`. IPC caller authentication, prompt correlation and
session ownership need implementation before it may change trust. Discovery
records are candidates only; protocol Hello never establishes trust.

## Host tooling and constraints

Observed: Rust 1.99.1, Cargo 1.99.1, protoc 36.1, avahi-browse installed;
pkg-config reports avahi-client 0.9-rc5 and libei 1.6.0. These are host observations,
not new dependency pins or proof of an active daemon, firewall reachability,
portal permissions, or KWin protocol support. No secret-store adapter was found
in first-party sources. The native development session explicitly stops some
wallet services, so secret-store availability must not be assumed. Protected
0700 state directories / 0600 key files with symlink-safe creation are required
fallbacks; fail closed on unsafe ownership or permissions.

## Builds, checks and risks

Run `dotnet build DaisyOS.sln --no-restore`,
`dotnet test tests/DaisyOS.Core.Tests/DaisyOS.Core.Tests.csproj`, and the full
`scripts/test.sh` release gate. `scripts/publish-shell.sh --fast` and
`scripts/run-kwin-prototype.sh` are the documented nested-session workflow.
See [TEST_MATRIX.md](TEST_MATRIX.md) for actual results and manual checks.

This change establishes milestone 0 policy/contracts and preserves the master
plan. Daemon, IPC, secure pairing, discovery, clipboard and file transfer are
not implemented by these documents or the isolated core policy. Universal Input,
handoff and streaming remain later milestones. No service is installed or
network port opened. Do not expose UI toggles that suggest these features work.

Next integration risks: two sessions sharing one user bus; asynchronous revocation
races; lock signals arriving after data dispatch; clipboard selection lifetimes;
IPv6 link-local scope; canonical path checks versus symlink races; authenticated
resume manifests; cross-language generated bindings; package/release installation
and independent service restart without a Settings window.
