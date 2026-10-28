# Continuity security and release gates

Status: requirements and isolated policy tests, not a security-reviewed service.
All real network feature exchange remains unavailable in this foundation.

Trust must originate in reviewed verified pairing with explicit local approval
on both devices. Hostname, address, TXT ID, self-signed certificates, Hello claims
and matching display names cannot establish trust. Neither LocalSend's permissive
certificate validation nor DaisyOS's debug/mock password bypass is a Continuity
authentication primitive. Debug builds must enforce the same peer checks as release.

Use reviewed identity, certificate binding, authenticated pairing and TLS peer
verification libraries. Selecting an actual reviewed pairing construction and
verification implementation is outstanding and blocks real peer data. Do not
implement a home-grown SAS hash, PIN exchange, key agreement or accept-all TLS
callback to get an end-to-end demo. Pairing sessions need finite lifetimes,
role/transcript binding, replay resistance, quotas and local prompt correlation.

Per-peer permissions default off. Receiver checks current trust, authenticated
connection, enabled capability and active unlocked graphical session before
dispatch. File writes also need unexpired consent scoped to peer, transfer ID,
manifest and chosen destination. A generic consent boolean cannot substitute
for those bindings; the isolated policy accepts only already-validated inputs.
Pause, disable, lock, user switch, logout, owner loss, disconnect and revocation
must cancel sensitive in-flight operations and invalidate session grants. Recheck
before clipboard publication or atomic file commit to close asynchronous races.

## Storage and file safety

Proposed paths: `$XDG_STATE_HOME/daisyos/continuity` for private identity/trust/
resume records, `$XDG_CONFIG_HOME/daisyos/continuity` for nonsecret preferences.
Apply XDG fallbacks under the user's home when unset. Directories 0700, files
0600; refuse unsafe ownership, symlinks and unexpected record versions. Secret
Service may be used if available; no existing first-party secret adapter exists.
Separate identity material from ordinary settings backups; restoring/cloning
an installation must not silently clone identities. Persist no clipboard history.

Files must land in receiver-selected storage using safe basenames and symlink-safe
operations anchored to a directory handle. Reject absolute paths, traversal,
separators, NUL/control characters, device files and directory offers. Reserve
space, limit sizes/counts/concurrency, stream hashes, stage without overwriting
existing files, verify digest, atomically publish. Cancellation/disk-full/crash
must leave no apparently completed corrupt file; quarantine resumable partials.
Peer-provided MIME and display names never authorize execution or path selection.

## IPC and session boundary

Versioned session D-Bus does not by itself prove that a method invocation is
an approved local user gesture. Validate caller UID/session and response ownership,
bind prompts to live requests, and reject stale replies after owner changes.
Handle multiple graphical sessions per user explicitly; do not expose clipboard
or UI prompts to the wrong session. No service should activate networking when
there is no authorized graphical session. UI not running must not imply consent.

Pure policy state is not cryptographic verification or an in-flight cancellation
mechanism. There is deliberately no installed service or UI enable switch yet.

## Required security tests before enablement

Impersonation, forged transport certificates, identity change, downgrade,
replayed confirmations, duplicate pairing, unexpected requests, quotas, permission
revocation races, lock during clipboard/file commit, daemon/IPC owner loss,
malicious lengths and UTF-8, path traversal/symlink races, disk exhaustion,
resume manifest tampering and unknown persistent schema versions.

Input remains disabled until compositor/portal capture AND injection are supported
with local target consent and a compositor-reserved emergency shortcut. Never
use /dev/uinput or remote lock-screen control. Keep optional streaming separate.

Discovery leaks device presence on shared LANs even with encrypted data. Provide
visibility controls and advertise no private payloads. Log only stable safe event
codes and operational metadata; no contents, private keys, pairing secrets or
authentication tokens. Same-user local malware is outside the daemon's isolation
claim. Do not open WAN routes, disable firewalls or introduce an HTTP control API.
