# Continuity security status

The MVP has implemented security boundaries and local automated coverage. It is
**not independently security-reviewed or accepted on two real devices**. Input,
handoff and display capture are unavailable. Preserve those release gates.

## Identity, TLS and pairing

An independent ECDSA P-256 identity is generated with platform cryptography for
each user installation. Its self-signed client/server certificate binds identity
to TLS; device ID is the full certificate's SHA-256 fingerprint. Private material
lives in a protected state directory, not discovery, telemetry or UI payloads.
TLS 1.3 supplies fresh ephemeral transport keys. The identity certificate lasts
five years; replacing/renewing it changes the pin and requires new pairing.

Unknown roots are candidate identities only. The platform X509 verifier validates
possession/chain/usage/expiry, and actual peer certificate bytes must match the
bootstrap. Existing peer connections must match remembered identity before any
feature dispatch. There is no accept-all certificate callback, global shared key,
bare PIN encryption, custom key exchange or truncated SAS construction.

Pairing uses conventional full certificate-fingerprint verification on both physical
devices, followed by explicit local approvals over that mutually key-authenticated
channel. The user compares both complete fingerprints in both directions. This
requires careful human verification; it is less compact than a reviewed PAKE/SAS
flow and must not be described as audited. A name/IP/discovery record is never proof.
Unknown candidates receive no capability data. Consent expires, is correlated with
a live request, and cannot survive revoke/owner loss. Fresh pairing after revoke
requires new approval; a trust epoch prevents stale approvals restoring authority. Persisted
file consent and completion receipts also bind to the current permission-grant ID,
so a later re-pair or disabled permission cannot revive an old receive approval.

## Receiving-side policy

Sharing requires global enable, active authorized user session, no pause/lock,
current trust and the peer's explicit capability grant. Clipboard and files default
off after pairing; both endpoints enable their respective permission. File writes
also require receive consent, scoped to the exact authenticated manifest, or its
unexpired specific resume approval. Support negotiation grants no permission.

Shell authorization comes from a long-lived unique D-Bus owner, not the Settings
window. logind Active/State/LockedHint and suspend changes are monitored. Lock/pause,
logout, user switch, bus loss, owner loss and revoke cancel operations; final file
publication and clipboard-source setup also synchronize against cancellation.
There is no pre-login sharing, remote unlocking or remote input. An unavailable
session authority fails closed. A second same-user graphical session cannot own
the same shell authority name.

D-Bus method callers must have the same UID. Session-grant methods additionally
require the shell authority's actual unique owner. Same-user local malware remains
outside this IPC isolation claim; a session bus is not an application sandbox.
The internal dependency seam used by tests substitutes session presence and a
private Downloads directory. Program never exposes that seam through a command,
environment variable, Debug mode or user setting; real TLS checks remain enabled.

## Storage and transfers

State directories must be user-owned real directories with no group/other access;
private files are 0600 and directories 0700. statx checks type, owner, permissions
and symlinks before loading. New private writes use unique exclusive temporary
files, fsync and atomic rename with parent-directory synchronization. Startup probes
state writability; failure to persist settings disables sharing and invalidates the
old on-disk settings where possible rather than silently retaining old enabled trust. Key bytes exported during creation are cleared afterward.
Identity is separate from ordinary preferences and must not be cloned/backed up
as normal settings. Unknown schemas/unsafe existing state fail startup.

Network offers supply basenames only. Reject traversal, absolute/separator paths,
controls, oversized names, lengths and offsets, special source files and concurrent
manifest collisions. Staging and final private directories reject symlinks; outputs
use exclusive temporary names and no-overwrite publication. Partial files are never
presented as completed files. Hash mismatch removes staging/approval. File opening
is only through the local user's notification action, never a network command.

Bounds include 8 incoming sockets, 2 incoming files, 2 outgoing files, separate
clipboard slots, 16 prompts, 64 peers, 1 MiB text, 64 KiB chunks, 100 GiB/file,
finite consent/handshake/inactivity deadlines and a bounded expiring clipboard ID
cache. File buffers do not grow with file size. No root, /dev/uinput, HTTP control,
WAN routes, subnet scanning, firewall disablement or debug authentication bypass.

Clipboard is potentially sensitive. No payload logs/history/state; source ownership
is dropped on lock/pause/revoke, and local contents are not exported while disabled.
Explicit text selection only; MIME/digest/UTF-8 are checked. Clipboard-adapter hints
about sensitive content are not reliable secret detection. Managed temporary strings
and third-party selection processes prevent claiming complete memory zeroization.

## Outstanding release evidence

Negative local tests cover changed pins, default-disabled storage, unsafe state,
malicious filenames/public addresses, denied file permission, digest corruption,
real TLS1.3 mutual authentication, specific-manifest resume, and Core lock/revoke/
late-callback rules. The service smoke test uses an isolated user bus with sharing
disabled and verifies clean SIGTERM. These do not establish complete MITM/pairing UI,
real clipboard ownership, disk exhaustion, symlink races under local compromise,
roaming/suspend/crash recovery or production accessibility acceptance.

Before enabling in a release, obtain independent pairing/protocol review and run
TEST_MATRIX.md on two real DaisyOS devices, including full fingerprint rejection,
replay/downgrade, revoke/lock races, disk-full, crash-resume and malformed frames.
Never claim these unperformed checks passed. Logs use stable safe codes and omit
private keys, clipboard/file payloads and authentication material.

## Input/media/handoff extension

Input, handoff, screen and audio grants are independent and default off, including
when loading older trust records. Target session consent is required for each
request; input and screen additionally use the public desktop portals and their
visible local stop indicators. Focused viewer input is forwarded through libei,
never root/uinput or global interception by an ordinary Wayland client. Session
lock, pause, revocation and transport loss cancel live sessions and drop input
emulation. JPEG dimensions, audio packet lengths, input ranges/sequences and
activity schema/content sizes are bounded. A private marker enables recovery of
the daemon-created audio sink after a crash. These paths require real-device
acceptance testing; see [MEDIA_AND_HANDOFF.md](MEDIA_AND_HANDOFF.md).
