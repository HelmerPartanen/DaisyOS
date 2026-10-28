
DaisyOS Continuity — Master Implementation Plan

Target: DaisyOS desktop + DaisyOS laptop on the same reachable local network, including a desktop connected by Ethernet and a laptop connected by Wi-Fi.

Audience: Codex working within the existing DaisyOS source repository.

Status: Implementation specification, not a claim that the system already exists. Codex must inspect the actual repository and record the current compositor/shell architecture before writing production code.

Product principle: Continuity must feel native, automatic after explicit pairing, reliable, private, and nearly invisible when unused. Never sacrifice input safety or trusted-device authentication for convenience.
0. Outcome and scope

A user signs in to DaisyOS on two machines, enables Continuity, sees the other machine in Settings, securely pairs the devices once, and then can use permitted features over their LAN. Neither internet, a cloud account, nor Bluetooth is required. A router typically bridges the Wi-Fi and Ethernet networks. A guest network, client isolation, firewall, or separated VLAN may block discovery or connections.

MVP (build first):

    Automatic discovery of nearby DaisyOS peers via mDNS/DNS-SD, plus an IP/hostname fallback.
    Explicit pairing, persistently remembered device identity, encrypted authenticated connections, revocation.
    Text clipboard sharing with loop prevention and clear privacy controls.
    Fast, resumable file sending with accept/reject, progress, safe filenames, and checksums.
    A polished native Settings page and reliable per-user background service.

Second release:
6. Universal pointer/keyboard control across computers with compositor/portal authorization and emergency local recovery.
7. Native-app handoff using versioned activity descriptors.
8. Multi-monitor layout editing and optional richer clipboard types / file drag-and-drop.

Later, optional:
9. Remote screen viewing, laptop-as-display streaming, audio routing, multi-peer group topology. True display extension requires a virtual output or other compositor integration; it is NOT equivalent to simply transmitting a video feed.

Explicit non-goals for the MVP: moving a running process and its memory, remote-login or remote-unlock, WAN access, exposing devices to the public internet, clipboard history synchronization, arbitrarily controlling third-party apps, mandatory cloud services, or global keyboard interception from an ordinary Wayland client.
1. Mandatory repository inspection (Codex: do this before editing)

    Determine whether DaisyOS currently runs on C# + Avalonia + KWin, a custom Smithay compositor, or another evolving combination. Read the actual solution/workspace files, startup logic, input handling, clipboard abstractions, Files app, Settings app, user session services, logging facilities, and automated tests.
    Identify the existing languages, async runtimes, dependency policies, IPC conventions, secrets storage, service lifecycle and packaging. Do not assume an architecture from prior conversations.
    Find the integration points: system Settings, taskbar/systembar notifications, file manager context menu, Wayland compositor or portal adapter, application launch/deep-link API.
    Produce docs/continuity/REPO_INTEGRATION.md with real paths, modules to modify, build commands, risks, and implementation choice. If a proposal conflicts with the repository architecture, adapt the plan with a short explanation.
    Preserve existing public APIs and UX; avoid unrelated refactoring. Do not start by creating a replacement compositor, rewriting the shell, or building video streaming.

Default architecture when compatible: Rust Tokio service for encrypted networking (quinn / rustls), discovery via system Avahi D-Bus, shared versioned .proto wire contracts, session D-Bus for UI integration, and thin adapters for existing C#/Avalonia or compositor components. If DaisyOS already has a mature equivalent service stack, prefer adapting it over introducing a duplicate runtime. Versions must be pinned to current mutually compatible stable releases at implementation time; do not invent dependency versions.
2. Architecture and process boundaries
DESKTOP: DaisyOS running on Ethernet               LAPTOP: DaisyOS running on Wi-Fi
┌────────────────────────────────┐                 ┌────────────────────────────────┐
│ Settings, Files, native apps   │                 │ Settings, Files, native apps   │
│ Compositor or portal adapters  │                 │ Compositor or portal adapters  │
│         │ session IPC          │                 │         │ session IPC          │
│ continuityd (unprivileged)     │◄── QUIC/TLS ──►│ continuityd (unprivileged)     │
│ - discovery / peers / pairing  │  LAN through    │ - discovery / peers / pairing  │
│ - policy / clipboard / files   │  home router    │ - policy / clipboard / files   │
│ - capability router            │                 │ - capability router            │
└────────────────────────────────┘                 └────────────────────────────────┘
             ▲                                                  ▲
             └───────────── mDNS/DNS-SD discovery ──────────────┘

Process ownership

    continuityd: per-user, systemd user-managed service; discovers peers, authenticates, maintains connections, handles permissions and feature data. No root privileges and no dependency on an open Settings window.
    Continuity UI: existing DaisyOS Settings UI and a small systembar indicator. It talks to continuityd through versioned local IPC. It cannot silently bypass pairing/consent.
    ClipboardAdapter: privileged with respect to clipboard availability, but not root. Prefer compositor-owned integration or ext-data-control-v1 where supported and authorized. Explicitly manage Wayland selection lifetimes and MIME types.
    InputBridge: compositor-owned feature, or permission-mediated InputCapture/RemoteDesktop portal + libei. Handle capture and injection separately. Never simulate global Wayland control by assuming a normal Wayland client can read arbitrary input.
    FileTransferManager: bounded disk IO, incremental hashing, temporary files, safe destination decision.
    HandoffProvider per app: opted-in activity capture/restoration.
    DisplayStreamer (future): separate optional module with screen capture, encoding, transport, decoding, display integration.

Prefer one encrypted peer connection, multiplexed by feature-specific streams. Use explicit feature authorization and per-channel backpressure. Keep rendering/input threads free of blocking networking, serialization, filesystem operations and locks.
3. Local connectivity and automatic discovery
3.1 Network model

    Desktop may be 192.168.1.20 via Ethernet and laptop 192.168.1.35 via Wi-Fi; illustrative only. No requirement that both use the same physical medium. They need IP reachability.
    Both machines advertise on appropriate active private LAN interfaces. Ignore loopback; avoid public-facing interface advertising by default. Support IPv4 and IPv6 (including correct interface scoping for IPv6 link-local).
    Do not add a cloud rendezvous service, public port forwarding, NAT traversal, or dependence on a web API.
    On network changes, Wi-Fi roaming, DHCP renewal, suspend/resume and daemon restarts, refresh addresses and re-discover. Re-authenticate peer identities after reconnect.

3.2 Protocol

Use mDNS/DNS-SD with Avahi's recommended D-Bus API on Linux, or reuse a matching existing discovery layer. Publish the service type:

_daisycont._udp.local.

Proposed TXT keys: pv=1 (discovery protocol version), cap=clipboard,files,input,handoff (advertised potential features), id=<stable opaque public device ID>. Device display names can be announced if the user explicitly permits local visibility; they are not secrets. Never publish IP keys, pairing secrets, clipboard contents or authentication tokens in TXT records.

On the listener:

    Browse continuously but event-driven, not by frequent network scanning.
    Resolve hostname, addresses, port and TXT records.
    Ignore your own public identity and invalid records.
    Deduplicate across NICs, IP families and renamed services by stable peer ID.
    Treat all discovered peers as untrusted until cryptographic verification succeeds.
    Show reachable unpaired peers separately from trusted devices.
    Allow Connect by IP/hostname in case multicast is unavailable. This still requires the same secure pairing protocol.

Use a configurable UDP listener port, with a documented default such as 47621/udp, and advertise the actual chosen port. Configure any firewall permission narrowly; do not blindly disable a firewall. Avahi mDNS typically uses UDP 5353 and is separate from the Continuity QUIC port. Favor a single bound QUIC endpoint; avoid a second unauthenticated open control server.
3.3 UX

Settings → Devices → Continuity:

    Continuity on/off, Visible to nearby devices on/off, own machine display name.
    Nearby DaisyOS devices, Paired devices, Connect by address.
    Status labels: Discovered / Pairing / Trusted / Connected / Asleep or Unreachable / Error.
    Actionable troubleshooting: different network, guest Wi-Fi isolation, UDP firewall, daemon disabled.
    Gracefully handle DNS-SD collisions, stale advertisements, and duplicate entries.

Acceptance: With laptop on Wi-Fi and desktop on Ethernet behind one consumer router, each appears exactly once within a few seconds under normal conditions. Discovery succeeds without internet access. Manual hostname/IP connection works when mDNS is blocked but unicast traffic is permitted.
4. Identity, pairing and security (blocker for all data exchange)
4.1 Device identity

    Generate an independent cryptographically secure long-lived device signing identity for each installation/user context. Use a reviewed crypto library; e.g. Ed25519 for identity, with an explicit scheme to bind identity to transport certificate/public key.
    Store private keys in a supported desktop secret store when available; otherwise in a protected user-owned state directory with restrictive file permissions and no log/backup leaks. Never place keys in discovery TXT, UI telemetry or debug logs.
    A display name is cosmetic and cannot establish trust. Stable peer identity derives from the authenticated cryptographic key, not MAC, hostname, IP, or username.
    Generate and rotate short-lived transport session material according to TLS best practices. On reinstall or identity reset, require pairing again.

4.2 Pairing UX and verified cryptography

A user chooses Pair on one DaisyOS device; the other presents a local permission prompt naming the request. Show an authenticated comparison code/phrase on both and require local human verification and approval at both ends. An alternative is a short-code entry flow implemented with a vetted, correctly configured PAKE protocol (e.g. SPAKE2+-style approach) instead of transmitting a bare PIN. Never use an unauthenticated six-digit PIN as an encryption key or assume matching hostnames prove identity.

Implementation notes:

    Discovery produces an untrusted candidate only.
    Establish a temporary encrypted pairing channel using a reviewed pairing design. Bind the displayed verification value to the authenticated handshake transcript, both public identities, nonces and role information. Use a cryptographic library/established pattern; never invent an ad-hoc key exchange, SAS algorithm, or TLS verifier.
    Only after verified user confirmation, store the peer's transport-verification identity or certificate/key pin and explicit permissions.
    Future QUIC TLS 1.3 connections MUST authenticate to the pinned peer identity, reject mismatches, and authorize the requested feature. A self-signed certificate without pinning/authentication is not enough.
    Rate-limit pairing attempts, expire unfinished sessions, require renewed approval for key changes, prevent replay, and do not auto-accept unexpected pairing requests.
    Show Forget device and Revoke access, which immediately terminate its active sessions and delete trust records; it must re-pair to regain access.

4.3 Capability permissions

Store permissions per peer and enforce them in the receiving service AND compositor/portal boundary:
Capability	Default after pairing	Extra requirement
Discovery/connection status	Allowed	May hide device visibility globally
Text clipboard	Off until enabled	Visible indicator/toggle; no clipboard history
Sending files	Sender may request	Receiver accept/decline, unless explicitly trusted for auto-save
Remote pointer/keyboard	Off	Both sides opt in; local target-session grant; emergency stop
App handoff	Off until enabled	Target app must accept activity types
Screen/audio capture	Off	Explicit local consent per session or carefully scoped remembered permission

Treat clipboard text as potentially sensitive: password managers can put secrets on the clipboard. Allow quick pause, per-peer disable, pause on session lock, and clear memory buffers promptly. Do not claim automatic detection of secret content is reliable.

Security checklist: mTLS/pinning or equivalent mutually authenticated TLS; authenticated pairing; downgrade resistance; bounded, versioned messages; event quotas; no root daemon; least privilege; deny by default; no arbitrary commands from the network; validated paths and application IDs; sanitized display names; no remote lock-screen input; connection/session cancellation on lock and revocation; no WAN port opening.
5. Transport and protocol design
5.1 Transport

Choose QUIC (TLS 1.3) with the Rust quinn implementation when it fits the existing stack:

    One bidirectional client/server-capable QUIC endpoint per user device.
    Independent reliable ordered streams for control, clipboard, file metadata, individual file payloads and handoff.
    A separately budgeted low-latency input channel; initially use reliable ordered messages for keys and buttons, coalescing motion instead of queueing obsolete cursor deltas. Optimize with QUIC datagrams only when tests justify it and loss/reordering handling is implemented.
    Fair scheduling and bandwidth caps so a large file transfer cannot make pointer/keyboard traffic unusable. If needed, isolate media traffic into a separate connection with explicit priority.
    Bounded message sizes, finite timeouts, cancellation, keepalive, backpressure, max concurrent file streams, and no unbounded task spawning.

5.2 Versioned wire format

Define protocol/continuity/v1/continuity.proto (Protobuf supports both Rust and C#). Do not serialize Rust runtime structs or C# classes directly as an uncontrolled long-term network contract.

Suggested message families:
Hello         {protocol_major, protocol_minor, device_id, capabilities, nonce}
Ping/Pong     {request_id, timestamps for diagnostic RTT only}
PairRequest   {request_id, pairing_session_id, handshake_material}
PairResult    {request_id, status, verified_peer_id}
Permission    {peer_id, capability, allowed, expiry_or_session_scope}
ClipboardOffer{update_id, origin_peer_id, mime_types, size, digest}
ClipboardGet  {update_id, chosen_mime_type}
ClipboardData {update_id, mime_type, bounded_data}
FileOffer     {transfer_id, safe_display_name, length, digest, mime_hint}
FileDecision  {transfer_id, accept_or_decline, destination_policy}
FileChunk     {transfer_id, offset, byte_payload}
FileComplete  {transfer_id, verified_digest, status}
InputStart    {session_id, source_device, target_device, approved_screen_id}
InputEvent    {session_id, sequence, kind, payload}
InputStop     {session_id, reason}
ActivityOffer {activity_id, app_id, kind, schema_version, summary}
ActivityData  {activity_id, bounded_payload}
Error         {request_id, stable_code, safe_human_description}

This is a logical schema; Codex should select a coherent message envelope (message kind / request ID / protocol version) and write the real .proto, generated bindings and a documented framing scheme. Never mix plaintext JSON/control packets with authenticated QUIC data without clear separation. Unknown future fields should be safely skipped; incompatible major versions must fail clearly.
5.3 State machine
NotDiscovered -> DiscoveredUntrusted -> Pairing -> TrustedOffline
                                               -> Connecting -> Connected
Connected -> Reconnecting -> Connected
Connected -> Sleeping / Locked / Revoked -> Offline (restricted)
Unpaired/revoked -> untrusted only; no data or input

Only one elected connection should be active per pair (deterministic peer-ID ordering for duplicate simultaneous dials), and each peer's feature/session state must be separately tracked. Never authorize data just because a socket is connected. UI events are driven by state changes.
5.4 Reconnection

    Exponential backoff with jitter and a sane cap; avoid reconnect storms after router outages.
    Re-resolve DNS-SD addresses after network changes; do not pin an IP as identity.
    Invalidate remote input/control grants on disconnect and release any held buttons/keys on both ends.
    Resume approved file transfers only after authenticating the same peer and verifying the transfer manifest/digest.
    Keep remembered pairing across ordinary network disconnections, but not after a peer identity change.

6. Clipboard sharing (first end-to-end user feature)

MVP: UTF-8 text (text/plain;charset=utf-8, with safe MIME normalization). Rich text and images are later, opt-in extensions. Do not synchronize X11 PRIMARY selection by default: the clipboard should be the normal explicit copy/paste selection.

Flow:

    Clipboard adapter reports a new local selection with supported MIME types.
    Service assigns update_id, origin_peer_id, bounded size, hash, and ephemeral lifetime.
    Receiver validates peer permission, duplicates, format and size; on approval, provides the clipboard data as a local selection through an authorized Wayland/compositor integration.
    Remote-injected selection changes must be tagged so they are not re-broadcast back to their source (echo-storm prevention). Guard rapid toggling and simultaneous copy races.
    User pastes in any ordinary application using its own local keyboard shortcut. Pasting is not synthesized remotely.

Implementation cautions: Wayland clipboard transfers are often lazy streams, so a service must own the source and serve requests as long as the selection exists. Do not assume an xclip call will reliably support every Wayland session. Prefer the emerging ext-data-control-v1 for an authorized clipboard manager; investigate compositor capabilities and provide a compliant fallback if unavailable. Support copying from native and regular Wayland applications. Honor screen/session lock policy and zero sensitive cached data after use.

Limits: a conservative configurable text limit, such as 1 MiB initially; reject malformed UTF-8 or explicitly normalize; never log contents. Document whether copying text from a peer overwrites the target clipboard and expose an off switch.

Tests: copy A→B, B→A, Unicode/emoji/multiline, very rapid repeated copies, duplicate hash with independent user action, offline reconnect, app exit while copied source exists, password-manager-like clipboard, two-way simultaneous updates, no circular echo, lock/pause/unpair.
7. File transfer (second end-to-end user feature)

MVP UX: Files app → right-click selected file(s) → Send to DaisyOS device... → select paired peer. On receiver show sender, filenames, total size, and Accept / Decline. Show progress on both ends and success/failure in notifications. Files should arrive in Downloads/DaisyOS Transfers by default, or a user-picked directory. Require explicit acceptance for the first implementation.

Transfer strategy

    Exchange a manifest with opaque transfer ID, list of logical filenames, sizes and cryptographic hashes; negotiate limits and destination policy before streaming.
    Stream bounded chunks asynchronously (example 256 KiB–1 MiB per IO batch; tune from measurements) over one or several dedicated reliable QUIC streams. Avoid reading entire files into memory.
    Write to secure temporary files under the destination filesystem, flush and verify exact byte count + SHA-256 or equivalent digest before atomic rename. Clean up or preserve verified partials under an explicit retention policy.
    Support cancellation and resume using authenticated transfer ID, verified chunk/hash ranges, receiver-validated offsets, and consistent source metadata. Don't skip checks because a file already has a matching name.
    Preserve reasonable metadata where appropriate, but do not blindly restore ownership, executable bits, symlinks, special files, absolute paths, ../ path components, invalid UTF-8 paths, or device nodes.
    Handle name collisions predictably (filename (2).ext etc.), per-file errors and disk-full conditions. Refuse path traversal and symlink races; use safe directory-relative filesystem operations.
    Enforce configurable max concurrent transfers, byte quotas, receive-accept timeouts and free-space checks. Never auto-run transferred binaries.

Later: drag/drop through the file manager onto a peer icon; actual cross-device drag between generic third-party apps requires deeper compositor drag-and-drop / clipboard integration and should not be promised at MVP.

Tests: empty and multi-GB files, folders, small files, Unicode filenames, collisions, symlink payload attempts, malicious relative paths, checksum mismatches, sender crash, receiver crash, Wi-Fi drop mid-transfer, low disk space, very slow receivers, concurrent transfers and file modification during send.
8. Universal pointer and keyboard (compositor-dependent)

Goal: User arranges Laptop to the right of Desktop in Settings. When the locally controlled pointer crosses the desktop's configured right-edge barrier, DaisyOS captures the current input session and routes allowed pointer/keyboard events to the laptop. A visual boundary animation may be used, but it must not mask input failure.
8.1 Input control authority

If DaisyOS has a custom Smithay compositor: Implement barrier detection, local input handoff/capture and remote event injection in its trusted input subsystem, integrating with Smithay's seat/pointer/keyboard handlers. Expose narrow internal IPC to continuityd. Keep compositor focus, physical device ownership, cursor visibility and input routing coherent. Do not add raw networking to the render thread.

If DaisyOS currently uses KWin/other Wayland compositor: Prefer standard InputCapture portal for outward crossing/edge barriers, RemoteDesktop portal + libei for injecting input at the destination, if implemented by that specific backend. Verify real portal/backend support on the user's installed desktop: a documented portal API does not guarantee every compositor implements it. If unavailable, gracefully disable Universal Input until a supported compositor path is integrated. Never claim ordinary user-level Wayland APIs can globally intercept keys or move the pointer across arbitrary apps.

Do NOT implement a privileged /dev/uinput bypass or disable compositor security checks as a shortcut.
8.2 Screen layout

Define a persisted peer layout model:
Screen { stable_output_id, device_id, logical_x, logical_y,
         logical_width, logical_height, scale_factor, rotation }
Adjacency { from_screen, from_edge, to_screen, to_edge,
            allowed_edge_segment, mapping_policy }

    Use logical compositor coordinates for edge detection, independent of pixel resolution and scaling.
    Convert to normalized position along the crossing edge (t=0..1) and project onto the destination's matching edge; clamp to valid bounds and preserve offset only where screens overlap.
    Handle vertically offset displays, unequal sizes, 125–200% scaling, rotated monitors, and monitor hotplug. Invalid adjacency prevents handoff rather than guessing.
    Support a configured dwell/hysteresis or drag threshold to prevent accidental transfer near corners.
    Do not silently transfer an active locked pointer, pointer grab or sensitive drag: obey compositor focus/grab/gesture rules.

8.3 Input event pipeline

    Initially one active controlling device / one target at a time (no multiple ownership races).
    Capture pointer motion, buttons, scroll, key down/up, and modifiers with stable device/session sequence numbers.
    Keyboard scancodes/key symbols and layout translation must be defined explicitly. Preserve modifier state and keyboard layouts between heterogeneous devices; never guess that matching raw keycodes guarantee same characters.
    Coalesce redundant movement under load and prevent stale input replay. Keys and button state changes require reliable ordered delivery.
    On disconnect, timeout, lock, revoke or emergency key: immediately restore local control, clear capture, and synthesize necessary release events so no key/mouse button remains stuck remotely.
    Do not forward input to the lock screen, password prompts requiring secure attention, or unauthorized privileged contexts. Stop on session lock or user switch.
    Install a compositor-reserved emergency shortcut that always works locally and cannot be forwarded to a remote device; display it in Settings. Design the shortcut after auditing reserved keybindings.
    Cross-device cursor ownership, hide/show, hardware cursor pipeline, low-latency paint feedback, motion acceleration, natural scrolling and click-drag behavior must match DaisyOS UX.

Performance targets (engineering goals, not network guarantees): avoid large queue buildup; aim for local-LAN pointer responsiveness without visible stutter and measure p50/p95 input RTT / edge-crossing delay. Require no hangs or trapped input under packet loss, Wi-Fi roaming or target sleep.
8.4 Input safety tests

    Edge transfer both directions, screen placements left/right/top/bottom.
    Drag from desktop to laptop (ordinary pointer operation) and back.
    Held Shift/Ctrl, keyboard repeat, mouse buttons and wheel while crossing.
    Drop network during a held key/button; both desktops recover cleanly.
    Remote device locks, logs out, sleeps, reboots or revokes grant.
    Emergency local shortcut works despite dead daemon/network.
    All monitor DPI/rotation permutations, hotspot/roaming, 144 Hz source to 60 Hz target.
    No unwanted transfer at edge when game grabs mouse / pointer is confined.

9. App Handoff API (native applications first)

Concept: Handoff is activity restoration, not process migration. An app advertises a restorable activity descriptor on source (e.g. Files folder URI or browser webpage), and the target app receives it after explicit compatible-app verification.

Proposed application contract:
publish_activity(Activity {
  app_id: "org.daisyos.Files",
  kind: "folder-view",
  schema_version: 1,
  title: "Documents",
  payload: <bounded, structured app-owned data>,
  expires_at: ...,
  requires_resource_transfer: bool
})

on_restore_activity(activity) -> {handled | not_supported | needs_resource}

    App IDs should be application-registry identities, not arbitrary shell commands/paths supplied by peers.
    Cap descriptor sizes (e.g. 64 KiB), schema-validate and version them per activity type.
    Only foreground-relevant activities should be advertised, under a user-controlled setting. Make the destination selectable; show a native Continue on Laptop surface or dock/taskbar indicator.
    First providers: Files (folder-view), web browser (url-view, only if a compatible browser is available), editor (document-view, only when source is accessible).
    Local resources are not magically shared: if a document only exists on desktop A, the target must have a shared path or ask to securely transfer an approved copy.
    Local app handles the actual restore, respecting user login, file permission, focus, and application-specific state.
    Do not export private tokens, unsaved editor buffers, cookies, arbitrary local file paths, authentication headers or browser session secrets by default.

Test incompatible app versions, no installed app, duplicate restore requests, stale activity, source offline, unsaved files, and malicious activity payloads.
10. Optional laptop as display / remote screen (separate milestone)

Distinguish three products:

    Remote screen viewing: Capture a monitor/window on desktop and display video in a laptop window. The desktop still owns that screen.
    Mirror display: Fullscreen or resizable streamed copy of desktop's existing output, optionally with separately authorized remote input.
    Extend desktop: Desktop compositor creates a virtual output whose encoded pixels stream to the laptop. Windows can move onto that virtual display. Requires virtual output, display topology, focus/input mapping and low-latency video pipeline support, not merely ScreenCast.

Implementation approach: PipeWire capture (via ScreenCast portal where appropriate; for a custom compositor, a permission-aware internal capture source), GStreamer / hardware video codec where available, authenticated transport negotiated through Continuity, low-latency decode on receiver, display synchronization and adaptive bitrate. Evaluate WebRTC over host-only LAN candidates for mature real-time media transport, or a properly engineered QUIC media data path. Avoid relying on retransmitting stale video frames over long reliable queues. Test HDR/color profiles, HiDPI, frame pacing, cursor metadata, damage-based updates and wake/lock behavior.

Do not make screen streaming a prerequisite for clipboard, files or Universal Input.
11. User interface / integration requirements
11.1 Settings

Settings → Devices → Continuity should include:

    Main switch and local discoverability.
    Local device name and security status.
    Nearby and paired peers, online/offline indicators, connection troubleshooting.
    Pairing dialog with verification and consent, revoke/forget action.
    Per-peer toggles: Clipboard, Files, Keyboard & Mouse, Handoff, Display Streaming (later).
    Peer screen arrangement editor (release 2): drag screens around, edge handoff zone highlighting, multi-monitor awareness.
    Descriptive no-internet requirement text: "Works across local Wi-Fi and Ethernet. Both devices must be reachable on the same local network."
    Global Pause Continuity one-click kill switch and status icon only when useful.

11.2 Shell and Files integration

    Files: Send to ..., cancel and progress, completed-file notification with Open containing folder.
    Systembar: subtle active-device indicator for active remote input / streaming; no permanent intrusive toolbar.
    Toasts: paired, connected, clipboard paused, request to receive files, transfer failed, device unreachable.
    Accessibility: keyboard navigation, screen-reader labels, non-color-only indicators, reduced motion and high-contrast compatibility.
    UI rendering, animations and pairing flow must use DaisyOS existing design tokens; do not introduce a mismatched design system.

11.3 Service state persistence

    Store own identity, peer trust records, per-peer features, monitor arrangement and transfer resumable metadata in appropriately separated secure paths under XDG state/config directories.
    Session-level capabilities expire on lock/logout, target disable and explicit revocation.
    Migrations for persisted record versions; backup/restore guidance (do not accidentally duplicate identities onto two machines).
    If no UI session exists, discoverability and data sharing should be off unless the product explicitly opts into a secured pre-login design (not planned).

12. Proposed repo structure (ADAPT to reality)
src/
  Continuity/
    README.md
    protocol/continuity/v1/continuity.proto
    core/
      identity/
      pairing/
      discovery/
      transport/
      trust/
      policy/
      peers/
      diagnostics/
    features/
      clipboard/
      file_transfer/
      input/
      handoff/
      display/               # future
    adapters/
      session_ipc/
      wayland_clipboard/
      compositor_input/      # or portal_input/
      files_integration/
    tests/
      unit/
      integration/
      fault_injection/
  Settings/...               # existing Settings feature, not a new UI framework
  Files/...                  # existing Files action hooks

docs/continuity/
  REPO_INTEGRATION.md
  PROTOCOL.md
  SECURITY.md
  TEST_MATRIX.md
  OPERATIONS.md

Treat these as logical module boundaries, not required directories. Preserve the existing codebase conventions.
13. Detailed incremental Codex execution plan

Every milestone must end with: real compile/test output, a brief change list, the exact files changed, a manual test script, remaining limitations, and a regression check. Avoid checking in feature stubs presented as finished functionality. Commit changes in small reviewable units.
Milestone 0 — Audit and minimal design (no behavior changes)

    Inspect actual repo, compositor and service startup; document interfaces and design decisions.
    Verify actual system dependency availability and toolchain versions.
    Agree on wire version / feature negotiation / state diagrams / peer record schema.
    Establish feature flags and a testable mock transport/peer abstraction.
    Add REPO_INTEGRATION.md, PROTOCOL.md, SECURITY.md, TEST_MATRIX.md.

Done: no guessed class/path names; documented integration points; existing builds and tests pass.
Milestone 1 — Daemon, IPC and mock peers

    Create minimal per-user service with explicit start/stop and state management.
    Add IPC from Settings/backend, lifecycle notifications and structured diagnostic logging.
    Add peer state manager and fake in-memory peers to test without two computers.
    Use test-controlled time/network interfaces for deterministic reconnection tests.
    Handle SIGTERM, suspend, logout, daemon crash/restart, UI not running.

Done: two local test instances can exchange Hello in isolation, and UI can display simulated states without freezing.
Milestone 2 — Network discovery / live connectivity

    Implement Avahi DNS-SD advertisement and browsing.
    Add UDP QUIC listener/dialer; stable peer deduplication; IPv4/IPv6, network-change handlers.
    Add manual peer address fallback and diagnostics.
    Add offline/reconnect behavior with backoff and proper cleanup.

Done: laptop over Wi-Fi discovers desktop over Ethernet and vice versa with internet disconnected; discovery alone grants no access.
Milestone 3 — Secure identity and pairing

    Generate/store identities safely and bind to TLS peer verification.
    Implement reviewed verified pairing handshake and user confirmation on both devices.
    Persist trust, connect only if identity matches, revoke/forget and validate permissions.
    Add negative tests for MITM simulation, spoofed peer names, key change, replay, unauthorized input/clipboard/files and protocol downgrade.

Done: forged or unpaired peer cannot invoke any capability; encrypted, authenticated reconnection works after reboot.
Milestone 4 — Text clipboard

    Implement authorized Wayland/compositor clipboard adapter with streaming selection ownership.
    Add per-peer toggle, bounded MIME / data handling, origin IDs, echo suppression.
    Add lock/pause/revoke behavior and UI status.
    Test common apps, repeated copies, huge text, emoji, concurrent copies, reconnections.

Done: copy from desktop, paste on laptop; copy back; no duplicate loops; off means off; no clipboard text in logs.
Milestone 5 — Files and transfer UX

    File manifest + receiver consent + chunked verified transfer + cancellation.
    Implement size limits, safe paths, folder tree policy, hash checking and atomic completion.
    Wire Files Send to DaisyOS device... action and both-side progress notifications.
    Add resumable transfers, crash recovery and disk pressure tests.

Done: safe multi-GB Wi-Fi/Ethernet file transfer with progress, verified data, no UI stalls and resumable interruption.
Milestone 6 — Universal Input (release 2)

    Choose real implementation based on compositor found in Milestone 0.
    For custom compositor: trusted edge barriers, seat routing and input injection; for KWin/other: probe InputCapture + RemoteDesktop + libei and fail disabled when unavailable.
    Build screen arrangement editor and normalized edge mapping.
    Add explicit target grants, emergency local escape, hold-state tracking and disconnect release.
    Test all screen topologies and fault scenarios above.

Done: keyboard and mouse reliably cross between laptop and desktop; network failure cannot trap input or leave stuck keys.
Milestone 7 — App Handoff (release 2)

    Add versioned native app registration, user-controlled active activity, compatible-app availability checks.
    Implement Files directory restore; add URL handoff for browsers where supported.
    Do not silently move private documents; integrate explicit file transfer on missing resources.

Done: Activity from supported app A can be explicitly continued in corresponding app B without exposing private data by default.
Milestone 8 — Polish, maintainability and rollout

    Complete settings accessibility, polished notifications, localization hooks, status/diagnostics.
    Profiling: startup, idle resource consumption, pairing latency, discovery latency, clipboard RTT, transfer throughput, input latency.
    Document firewall setup, service status, privacy/security model and limitations.
    Build reproducible packages; test clean install, upgrade, revoke and rollback.
    Gate feature flags until the relevant acceptance suite passes on two real devices.

Done: no regressions to normal DaisyOS login, lock, input, clipboard or files without peer present.
Milestone 9 — Optional video/display research spike (not part of MVP)

    Prove secure low-latency PipeWire/video capture and decode on supported hardware.
    Benchmark 1080p60 and other targets; measure encode/decode latency, frame drop rate and power.
    Research compositor virtual output required for true extended display.
    Keep separate from already-working Continuity features.

14. Tests, failure engineering and metrics
Test environments

    Primary real scenario: desktop DaisyOS ↔ Ethernet ↔ same home router ↔ Wi-Fi ↔ DaisyOS laptop.
    Same Ethernet LAN; both Wi-Fi; AP client isolation ON (expected discovery failure); internet disconnected; router reboot; Wi-Fi disconnect/reconnect; IPv4-only; IPv6 dual-stack; VPN interface present; multicast blocked but unicast permitted.
    Screen lock/unlock, logout, suspend/resume, two user sessions, service crashes, process resource exhaustion, mixed versions.

Test layers

    Unit: parsing, limits, state transitions, policy, peer dedup, paths, clock/reconnect logic.
    Protocol property/fuzz: malformed envelopes, unknown versions, integer overflow, invalid lengths, hostile peer offers, never panic.
    Integration: two local service instances with synthetic clipboard/files and simulated network faults.
    Security: impersonation, forged certificates, changed keys, replay, rejected capabilities, pairing race, denial of service, excessive clipboard and transfer requests.
    E2E: two actual DaisyOS devices; verified clipboard, file transfer, edge crossings and recovery.
    Performance: p50/p95 discovery after device online; clipboard copy-to-ready time; transfer throughput/CPU; input event delay; idle RAM and CPU; cold login overhead.

Initial engineering targets, not promises:

    Idle service should do no busy-polling; strive for practically negligible CPU usage.
    Discovery visible within ~3 s under an ordinary healthy local network.
    Short text clipboard available on peer within ~200 ms under a healthy LAN.
    Pointer traversal should appear immediate to a human user, preferably within a frame or two on each display; measure on real devices and adjust scheduling before declaring success.
    No file-size-proportional memory growth; no compositor/render thread blocking.
    Revocation and emergency stop effective immediately at the user/session boundary even if remote networking is stalled.

Every metric must state hardware, network, loads and measurement method; do not fabricate benchmarks.
15. Operational / security quality bar

    Opt-in device visibility, trusted-peer list and prominent revoke/pause controls.
    Explicit receive consent and feature capability checks on each inbound request.
    TLS peer authentication and correctly verified pairing are release blockers.
    Locking either device ends interactive input and stops sensitive clipboard sharing.
    No listening HTTP APIs and no unnecessary privileged processes.
    Stable logging categories and safe error codes; no clipboard payloads, file contents or secret material in logs.
    Debug mode must not relax authentication or bypass user consent.
    Updates must preserve protocol compatibility or report clear incompatibility.
    Network privacy warning: on a shared untrusted LAN, discovery broadcasts device presence even though data channels are encrypted; offer visibility controls.
    Document that desktop/local malware running as the same trusted user can have capabilities outside this service's threat model; do not overstate isolation.

16. Key technology references for Codex

Consult current docs and choose compatible stable versions; do not blindly copy example code or dependency versions:

    DNS-SD service naming (max 15 characters in the service name): https://www.rfc-editor.org/rfc/rfc6763.html
    Avahi DNS-SD/mDNS: https://avahi.org/doxygen/html/ and
    https://github.com/avahi/avahi
    Quinn QUIC: https://docs.rs/quinn/latest/quinn/
    Smithay seat/input and selection modules: https://docs.rs/smithay/latest/smithay/wayland/ and https://smithay.github.io/smithay/smithay/input/index.html
    Wayland ext-data-control-v1: https://wayland.app/protocols/ext-data-control-v1
    Wayland libei/libeis: https://libinput.pages.freedesktop.org/libei/
    XDG InputCapture portal: https://flatpak.github.io/xdg-desktop-portal/docs/doc-org.freedesktop.portal.InputCapture.html
    XDG RemoteDesktop portal: https://flatpak.github.io/xdg-desktop-portal/docs/doc-org.freedesktop.portal.RemoteDesktop.html
    XDG ScreenCast portal: https://flatpak.github.io/xdg-desktop-portal/docs/doc-org.freedesktop.portal.ScreenCast.html

