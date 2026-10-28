# Continuity protocol foundation

Status: experimental v1 schema and tested C# frame codec. There is no live peer
transport. `protocol/continuity/v1/continuity.proto` is the source contract;
production Rust/C# generated bindings and reproducible generation are pending
selection of mutually compatible pinned runtime/codegen packages. Validate syntax:

```sh
protoc --proto_path=protocol --descriptor_set_out=/tmp/daisyos-continuity.pb continuity/v1/continuity.proto
```

## Transport and framing

Proposed authenticated QUIC endpoint: UDP 47621, ALPN `daisycont/1`. One endpoint
per active user session; port collisions must be handled before advertising the
actual bound port. Discovery: `_daisycont._udp.local.`, TXT `pv=1`, `id=<public-id>`,
`cap=clipboard,files`. Opt-in visibility, scoped LAN interfaces, no secrets in TXT.
Use system Avahi D-Bus, not subnet scanning. Service type follows
[RFC 6763](https://www.rfc-editor.org/rfc/rfc6763.html), and Avahi recommends its
[D-Bus API](https://avahi.org/doxygen/html/) for non-C applications.

Each envelope is a four-byte unsigned big-endian byte length followed by exactly
that many Protobuf bytes. Zero and lengths over 1,052,672 bytes (1 MiB + 4096)
are rejected before payload allocation. Clean EOF between frames is distinct
from truncated header/body errors. A partial write makes the stream unusable:
cancel/reset it rather than retrying a frame on the same stream. One writer per
stream; caller supplies cancellation, finite deadlines, aggregate memory quotas
and concurrency limits. The codec alone does not provide those service limits.

Every envelope carries major=1, minor=0 and a 16-byte random request ID. Major
mismatch terminates negotiation; unknown fields are skipped, unknown oneof kinds
and enum values grant no authority. Negotiate supported capabilities by intersection;
Hello identity must match authenticated transport, never discovery metadata.
Before negotiation only bounded Hello/error traffic is permitted. Pairing traffic
will need a separately specified, reviewed handshake on the same endpoint;
no permissive TLS verifier or bare PIN fallback is authorized by this schema.

One control stream; separate clipboard streams and one stream per accepted file.
Initial limits: 64 KiB control envelopes, 1 MiB UTF-8 clipboard data, four MIME
labels, 128 bytes per MIME label; 64 KiB file chunks, two active file streams,
16 pending offers, 255 UTF-8 bytes per display basename. IDs are 16 bytes,
device IDs and SHA-256 digests 32 bytes, safe errors 256 UTF-8 bytes. Validators
must enforce message-specific limits in addition to the global frame cap.
These validators, feature dispatch and stream routing are pending implementation.

Receiver owns destination choice. `FileDecision` never discloses a receiver path.
Transfers use a fixed approved manifest (peer ID, transfer ID, filename, length,
digest). Reject out-of-order/overflowing offsets and payloads past declared length.
Resume requires the same authenticated identity, unchanged manifest, approved
receiver state and verified partial data. FileComplete is sent only after digest
verification and atomic publication; no executable launch. Folder trees are
outside the first protocol and must be explicitly rejected.

Clipboard uses only the explicit clipboard selection; MIME normalizes to
`text/plain;charset=utf-8`. Validate strict UTF-8 and length/digest before
publishing. Scope update IDs to origin identity, bound their cache and lifetime,
and tag injected selections to prevent rebroadcast. Never persist payloads.

## State, permissions and persistence

State progression: undiscovered → discovered-untrusted → pairing → trusted-offline
→ connecting → connected → reconnecting/offline. Lock makes all feature dispatch
unavailable. Revocation removes trust and closes every active stream before
rediscovery can present an untrusted candidate. A connected socket alone is never
a trust transition. For simultaneous dials, the lexicographically smaller verified
public identity initiates the retained connection. Identity changes require
new local pairing on both machines.

Proposed peer record v1: schema_version=1, canonical identity scheme/public key,
transport binding/pin, sanitized display name, explicit clipboard/files permissions,
pairing time. Endpoint addresses and advertisement names are transient and
untrusted. Reject unknown record versions safely; migrate atomically and never
turn missing/unknown permissions into grants. Revoked records must not be
resurrected by pending callbacks or migration. Session consent, lock status,
clipboard data and network addresses are not persistent authority.

`ContinuityPolicy` is only a pure receiving-side decision helper. Its authentication
and trust booleans must be supplied by verified service state. It intentionally
has no Pair/Trust mutation method and cannot authenticate a caller. New policy
snapshots and cancellation are needed on every dispatch/state change.

## Deferred contracts

Pairing/SAS, local D-Bus introspection, permission mutations and their authorized
callers, daemon retry wiring, peer identity scheme/transport binding and migration
serialization remain unresolved. Do not ship this schema as a compatible stable
protocol until those contracts and cross-language conformance tests exist.
Input, handoff and display are not advertised, and core policy rejects them.

## Trusted-peer lifecycle prototype

`src/DaisyOS.Core/Continuity/ContinuityPeerSession.cs` now supplies a run-once,
per-trusted-peer controller with injectable `IContinuityPeerConnector` and
`IContinuityRetryDelay`. It starts offline, dials, checks the adapter's verified
identity, exchanges Hello and intersects the two MVP capability bits. Negotiation
does not grant clipboard/file permissions. Identity/protocol failures stop in
Error with a stable diagnostic enum; transient IO failures/disconnects use
exponential backoff (500 ms initial ceiling, 30 s cap, half-to-full jitter).
Backoff is not reset by Hello, so rapidly flapping connections cannot reset it.
Connect plus Hello have a ten-second cancellation deadline. Adapters must honor
cancellation and supply their own transport idle/dead-peer detection.

Revoke clears capabilities synchronously before cancelling the run. Late completion
cannot restore Connected. Stop clears features synchronously for lock/pause/logout. Dispose/caller
cancellation ends the run and releases the connection. Session integration must
call Stop and discard this object;
there is no automatic unlock resume or persistent trust mutation. The host must
supervise unexpected adapter exceptions and cannot treat a stopped run as active.

Snapshot revisions and a bounded, coalescing asynchronous state feed support a
future IPC/UI adapter without synchronous callbacks on network paths. The feed
is for one subscriber, not broadcast; consumers obtain Snapshot for current state.
The prototype's `ContinuityHello` is an in-process test model, not a serialization
contract or substitute for generated Protobuf validation. Its canonical 64-character
lowercase hex IDs represent the wire's 32-byte identities, whose cryptographic
binding is still pending. No constructor argument can cryptographically establish
trust: only the future daemon's verified trust store may create production sessions.
The test-only mock transport never opens sockets and is not packaged as a TLS bypass.
Connection election, discovery dedup, generated bindings, IPC, pairing, persistent
trust, keepalive and feature dispatch remain outstanding.
