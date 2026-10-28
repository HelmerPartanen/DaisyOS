# Continuity operations

The MVP implementation is opt-in and disabled by default. It provides a separate
unprivileged C#/.NET 10 daemon, native Settings/Files integration, Avahi discovery,
mutual TLS 1.3 pairing/connections, text clipboard and resumable single-file sends.
It has not passed the required two-machine/real-Wayland release acceptance matrix.
The subsequent input, Notes handoff, screen preview and audio routing extension is described in [MEDIA_AND_HANDOFF.md](MEDIA_AND_HANDOFF.md), including its additional build dependencies and limits.

## Build and install

```sh
dotnet test tests/DaisyOS.Continuity.Tests/DaisyOS.Continuity.Tests.csproj
dotnet test tests/DaisyOS.Core.Tests/DaisyOS.Core.Tests.csproj
scripts/build-continuity.sh
scripts/install-continuity.sh
systemctl --user daemon-reload
systemctl --user start daisyos-continuity.service
```

The standalone user payload requires .NET 10 including the ASP.NET shared runtime
(the existing System project references it), Avahi daemon/utilities and wl-clipboard.
The normal shell publish includes a self-contained `apps/Continuity` payload; Arch
packaging installs its user unit. Nothing in the installer starts sharing, changes
firewall rules or adds WAN access. Do not run the daemon as root. If installed in a
custom prefix outside systemd's unit search path, copy/link the generated unit into
`$XDG_CONFIG_HOME/systemd/user` yourself before daemon-reload.

Use the **native DaisyOS session**, with the current shell/session bridge. A normal
Settings process cannot grant session readiness. The shell's bus-name lifetime,
explicit lock updates, and logind Active/State/LockedHint gate data. No shell owner,
wrong/inactive session, suspend or bus loss disables sharing. The first daemon
startup is disabled even if no Settings window exists. If session authorization
cannot be established, fix the session rather than bypassing it with environment
flags. There is no debug authentication bypass.

## Pair and share

1. Open Settings → Continuity on both machines. Enable Continuity; make the receiving
   device visible. Visibility broadcasts its public name and identity on private LAN
   interfaces; encryption does not hide this presence.
2. Select the peer, or enter `hostname:47621`, `192.168.1.20:47621`, or
   `[fe80::1234%interface]:47621`. Both devices must be reachable; internet is unused.
   Use **Connect** for an already paired device at a new address; this authenticates
   its existing identity without resetting feature permissions. Use **Pair** only
   for enrollment/re-enrollment.
3. On **both physical devices**, compare the entire “This device” fingerprint against
   the “Other device” fingerprint shown on its partner, in both directions. Approve
   on both only if all characters match. These are full SHA-256 certificate
   fingerprints, not a short PIN or custom SAS. Requests expire after two minutes.
4. Enable Text clipboard and/or File sharing for that peer **on both devices**.
   Copying peer text overwrites the target's explicit clipboard. No history sync or
   PRIMARY selection sync occurs. wl-clipboard must have compositor-authorized
   data-control access; a missing/denied backend is shown unavailable.
5. In Files, choose **Send to DaisyOS device…** on a regular file, choose the paired
   device in Settings, and send. Receiver accepts the offer there. Progress and
   cancellation appear on both devices. Files finish in `Downloads/Continuity`.
   Directories are unsupported; archive them explicitly before sending.

Pause is persisted: restarting or crashing the daemon cannot silently resume sharing.
Forget/revoke terminates operations and removes trust; new pairing requires fresh
approval on both devices. Pause/disable/lock cancels sensitive operations and clears
remote clipboard source ownership. Local clipboard data is not cleared merely
because sharing was disabled. Remote input/capture features remain unavailable.

## Resumption and limits

Transfers stream 64 KiB chunks, with SHA-256 verification before atomic publication.
Defaults: maximum file 100 GiB, two incoming and two outgoing files, separate bounded
clipboard slots, eight inbound sockets, 1 MiB UTF-8 clipboard, 16 consent requests,
64 remembered peers. Staging is private user state, separate from final Downloads.
Existing destination files are never overwritten. Special files, traversal,
separators and unsafe basenames are rejected.

Transient file failures retry up to five times with capped jittered backoff. A
specific approved manifest can resume for up to 24 hours after a network/process
interruption. After a sender restart, choose the same unchanged file again; the
receiver recognizes its authenticated peer/name/length/digest and resumes. Explicit
cancellation, lock/permission failures or malformed/corrupt data invalidate approval;
new consent is required. Failed partials may remain for recovery; remove private
`transfers/*.part` and matching JSON only while the service is stopped if discarding
them. Crash recovery/large-file/disk-pressure behavior still needs hardware acceptance.

## Diagnostics and transport adaptation

```sh
systemctl --user status daisyos-continuity.service
journalctl --user -u daisyos-continuity.service
daisyos-continuity --status
```

The implementation reuses .NET TLS and the existing C# service stack. It uses **TCP
47621**, DNS-SD **`_daisycont._tcp.local.`**, and Protobuf inside TLS, rather than the
proposed QUIC/UDP transport. There is no HTTP API. mDNS uses Avahi's separate UDP
5353. If a firewall blocks traffic, permit the narrow TCP service port on the private
LAN profile; do not disable the firewall or open WAN forwarding. Guest Wi-Fi client
isolation, VLAN boundaries and multicast blocking can prevent discovery. Manual
connection works when unicast is allowed. Public network addresses are rejected;
IPv6 link-local scope is preserved.

An active per-operation TLS connection is authenticated independently; capability
support does not imply permission. Large transfers do not share the clipboard's
outgoing semaphore. This is not a persistent multiplexed QUIC connection. Other
open transport/rollout limitations are listed in TEST_MATRIX.md.

Private identity/trust/resume data lives below
`${XDG_STATE_HOME:-$HOME/.local/state}/daisyos/continuity` with 0700 directories and
0600 files. Do not clone identity.pfx across installations or include it in ordinary
settings backups. Resetting/replacing it requires new pairing. Unknown persistent
schema versions fail startup. Logs contain safe operational codes, never payloads.

## Clipboard fallback limitation

The current wl-clipboard adapter uses bounded digest-based echo suppression. A
genuine local re-copy of recently received text may be deduplicated for up to five
minutes, including after an intervening copy. This is a known MVP test limitation,
not finished clipboard semantics. An origin-aware native adapter is needed before
production acceptance. Different new text is unaffected.
