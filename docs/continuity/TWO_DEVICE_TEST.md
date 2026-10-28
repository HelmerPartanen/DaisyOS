# Two-device Continuity acceptance

This revision is prepared for **private two-device MVP testing**, not a claim of
completed release acceptance. Keyboard/mouse control, app handoff and display
streaming are outside this MVP and remain unavailable. Do not push automatically
from an agent; the user will push and pull the same revision onto the desktop.

## Install the same revision on both devices

Use the existing DaisyOS repository/native development setup on the laptop and
desktop. Build dependencies must already be installed (.NET SDK 10, the existing
Avalonia/KWin dependencies, Avahi with utilities and wl-clipboard).

```sh
git pull --ff-only
git rev-parse --short HEAD
dotnet build DaisyOS.sln
DAISYOS_APP_CONFIGURATION=Debug scripts/install-system-apps.sh
daisyos-native-development restart
scripts/build-continuity.sh
scripts/install-continuity.sh
systemctl --user daemon-reload
systemctl --user start daisyos-continuity.service
```

Restarting the native shell and updating standalone Settings/Files are necessary:
an older shell cannot supply the new session authorization, and an older Settings
window has no Continuity page. Close old standalone Settings/Files windows before
relaunching. The service installer changes neither firewall nor sharing settings.
Enabling Continuity in Settings also enables its user service for future graphical
sessions. Turning the feature off disables that startup choice while immediately
disabling data sharing in the running service.

If the desktop uses a packaged session instead, publish/install its shell package
with the same revision; `scripts/publish-shell.sh` includes `apps/Continuity` and
the Arch package installs the user unit. Do not mix old UI/session assemblies with
new daemon dependencies. The user installer above is sufficient for testing the
service alongside the updated native development session.

Run on each machine:

```sh
systemctl --user status daisyos-continuity.service
daisyos-continuity --status
```

Before enabling, status should show `enabled: false`. `sessionReady` should become
true in the new active native shell; outside that session it must stay false.
If authorization fails, verify the native shell was restarted, `XDG_SESSION_ID`
is valid, and logind reports your session active/unlocked. Do not bypass the guard.

## First acceptance pass

Record both commit IDs, hardware, KWin/.NET/wl-clipboard versions, network topology,
firewall configuration, display configuration and test time. The main topology is
desktop Ethernet ↔ home router ↔ laptop Wi-Fi. Disconnect internet after setup;
LAN sharing must continue working.

1. Enable Continuity and visibility on both devices. Each peer should appear once.
   If discovery fails, check Avahi and client isolation, then connect by hostname/IP
   (default TCP 47621). This implementation uses `_daisycont._tcp`, **not UDP/QUIC**.
2. Initiate Pair. On both physical machines, compare the **complete** “This device”
   fingerprint to the partner's “Other device” fingerprint in both directions.
   Decline one attempt and confirm neither side gains access. Repeat, verify every
   character and approve on both. Clipboard and File sharing must initially be off.
3. Enable Text clipboard for that peer on both devices. Copy ordinary text A→B and
   B→A in native and regular Wayland apps; include emoji, non-Latin text and multiline
   text. Also re-copy an earlier received value after an intervening copy: the
   current digest-based fallback can suppress this for five minutes (known limitation).
   No history/PRIMARY synchronization or repeated echo should occur. Use test
   text, not passwords. If data-control is unavailable, Settings must say so.
4. Pause through Quick Settings. Further copies must not propagate. Resume, then
   disable the peer clipboard toggle and verify it stays off. Lock either machine
   while copying: no new remote selection may be published while locked. Unlock and
   test normal local copy/paste regardless of network state.
5. Enable File sharing on both devices. In Files choose **Send to DaisyOS device…**
   on a small regular file. Decline once: no completed file should appear. Send again,
   accept, check progress on both machines, then verify SHA-256 of source and
   `Downloads/Continuity/<filename>`. The completion action should open that folder.
6. Send a duplicate name: existing content must not be overwritten. Try Unicode and
   long filenames. A directory should ask you to archive first; it must not silently
   traverse a tree. Never automatically open/execute received files.
7. Send a larger disposable file (start with 32 MiB, then 1 GiB or more as disk space
   permits). Verify no UI stall or file-size-proportional memory growth. Cancel from
   each end and check that partials are not published as completed files.
8. During another transfer, disconnect/reconnect Wi-Fi. Confirm bounded retry and
   resume at the correct offset with the same authenticated identity and manifest.
   After a daemon/process interruption, reselect the unchanged source file if needed;
   the target may require renewed consent, but existing verified partial bytes should
   be reusable. Check the final digest. Receiver disk-full and sender modifications
   must never produce an apparently completed corrupt destination.
9. Forget/revoke while copying/transferring. Sessions and source ownership must stop;
   reconnecting alone must not restore permission. Re-pairing and re-enabling Files
   must not reuse consent from the prior revoked permission grant.
10. Reboot/restart normally and confirm stable identities, remembered trust, and no
    automatic feature grants after identity reset. Test suspend/resume, logout/user
    switching, daemon crash, AP isolation, blocked multicast/manual connection,
    IPv4-only and scoped IPv6 where available.

## Evidence and troubleshooting

```sh
journalctl --user -u daisyos-continuity.service --since today
daisyos-continuity --status
```

Logs must not contain clipboard contents, file contents or private keys. Preserve
safe operation codes, timestamps and status; never attach identity.pfx. Describe
what action failed and whether it reproduced after restarting only the daemon.

Open TCP 47621 only on an appropriate private LAN firewall profile if required.
Avahi separately needs mDNS/UDP 5353. Do not disable the firewall, open WAN ports,
or relax certificate/session checks. Guest Wi-Fi/VLAN/AP isolation may prevent
reachability. File/clipboard permissions are required independently on both devices.

First successful local transport/integrity tests do not prove all of this matrix.
Record actual pass/fail results before calling the MVP production-ready. Independent
pairing/protocol review and the complete fault/accessibility matrix remain release gates.

## Input, handoff, screen viewing and remote audio

Connection and text clipboard sharing have now been verified by the user on the two physical computers. For the next revision, follow [MEDIA_AND_HANDOFF.md](MEDIA_AND_HANDOFF.md) for dependencies, installation, scope and the new acceptance checklist. Both machines need this revision; the four additional permissions default off.
