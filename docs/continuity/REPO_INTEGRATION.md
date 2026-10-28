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

The implementation now uses `src/DaisyOS.Continuity` as a separate .NET 10
executable, referencing the existing System/Core layers. It uses platform TLS 1.3
with certificate pinning/full-fingerprint pairing, versioned generated Protobuf,
Avahi's system D-Bus EntryGroup API plus the long-lived Avahi browser adapter, and
session D-Bus IPC. Settings/Files remain in the existing Avalonia design system.

This adapts the proposed Rust/QUIC stack: there is no existing Rust workspace,
libmsquic is absent, and reusing the existing C# runtime avoids a parallel daemon
stack. Network transport is TCP 47621 / `_daisycont._tcp.local.`, not UDP. Separate
bounded connections carry operations. See PROTOCOL.md for explicit framing and
security design. It is not wire-compatible with an imagined UDP implementation.

Actual IPC: `org.daisyos.Continuity1`, `/org/daisyos/Continuity1`, Execute(s)->s.
`src/DaisyOS.System/Continuity/ContinuityClient.cs` is the UI adapter;
`ContinuitySessionBridge.cs` supplies shell lifetime/lock grants.
`SettingsView.Continuity.cs` implements the Settings page; Files adds a separate
send action. Quick Settings exposes a pause/resume button only when enabled.
Identity/private state and consent enforcement belong to the daemon, not Settings.

Generated bindings live in `src/DaisyOS.Continuity/Generated`; the package runtime
and code generator are pinned and reproducible. There is no accept-all TLS callback.
Pairing verifies both complete certificate fingerprints on both physical devices;
it is implemented but still needs independent review and real-device acceptance.

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

The MVP code paths now exist: independent daemon, IPC, identity/trust, discovery,
manual address connection, pairing approvals, text clipboard adapter, consented
resumable file transfer, Settings/Files/Quick Settings, notifications and packaging.
The per-user service stays disabled for data sharing until explicit opt-in and
valid native-session authorization. No actual user service was installed or enabled
during implementation checks. The subsequent media/handoff extension adds focused viewer control, Notes text activities, screen preview and audio routing; see [MEDIA_AND_HANDOFF.md](MEDIA_AND_HANDOFF.md). Seamless screen-edge switching and virtual display extension remain unimplemented.

This is implementation progress, not completed release acceptance. Real-device
clipboard/network/fault/UI tests and independent protocol review are outstanding.
See TEST_MATRIX.md for exact evidence and remaining limitations.

Next integration risks: two sessions sharing one user bus; asynchronous revocation
races; lock signals arriving after data dispatch; clipboard selection lifetimes;
IPv6 link-local scope; canonical path checks versus symlink races; authenticated
resume manifests; cross-language generated bindings; package/release installation
and independent service restart without a Settings window.

## Second increment: trusted-peer lifecycle

Added `src/DaisyOS.Core/Continuity/ContinuityPeerSession.cs` and
`tests/DaisyOS.Core.Tests/ContinuityPeerSessionTests.cs`. The lifecycle can now
be exercised with two bounded in-memory mock peers and deterministic retry
scheduling. Snapshot changes are asynchronous and coalesced for a future adapter.
This completes the mock transport/retry test seam portion of milestone 0;
generated bindings and a passing full regression remain outstanding. It is an
isolated milestone 1 building block, not a daemon/IPC implementation. There is
still no Continuity service or UI activation to install or enable.
