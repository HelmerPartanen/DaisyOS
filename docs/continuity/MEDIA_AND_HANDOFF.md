> Historical implementation notes: the permission and input workflows below have
> been superseded by [SEAMLESS_CONTROL.md](SEAMLESS_CONTROL.md). Use that guide
> for current automatic, popup-free paired-device behavior.

# Continuity input, handoff, screen viewing and audio

> Current screen-edge control and streamlined audio/handoff flows: see [SEAMLESS_CONTROL.md](SEAMLESS_CONTROL.md). The original viewer-only implementation described below is historical where it conflicts with that guide.

The user verified device connection and text clipboard sharing on their desktop/laptop before this extension. The following features require updating **both** computers and new acceptance testing.

## What this revision implements

- **Screen viewing:** Settings → Continuity → paired computer → enable Screen viewing on both computers → View screen. The receiving computer approves the request and selects a monitor through the desktop portal. The viewer receives a 1280 × 720, up to 10 fps JPEG stream over a separate mutually authenticated TLS connection. Static screens use heartbeat frames. This is screen mirroring, not a virtual monitor or extended desktop.
- **Keyboard and mouse:** also enable Keyboard and mouse on both computers, then Control computer. The target grants RemoteDesktop portal access. libei injects physical keyboard, mouse position/buttons and scrolling into the authorized unlocked session. Keyboard layout is applied on the target. Input comes only from the focused viewer window. Ctrl+Alt+Escape, closing/deactivating the viewer, the target portal Stop button, locking, pausing, revoking permission or disconnecting ends control. This revision does **not** implement seamless screen-edge switching or a monitor-layout editor. Common typing/navigation keys are supported; media and keypad keys still need mapping.
- **App handoff:** enable App handoff on both computers. In Notes, right-click a note → Continue on another computer… → choose the paired computer. After approval, the target imports a versioned text snapshot as a new local Notes document. The original remains on the sender. Text is bounded to 64 Ki characters; rich formatting and attachments are not transferred. Other applications need their own opted-in activity providers. Executables, commands and sender filesystem paths are never accepted as activities.
- **Audio output:** enable Audio playback on both computers. Quick Settings → output devices lists discovered paired computers with audio permission using `continuity.svg`. Selecting a computer requests playback there. Approve it on that computer in Continuity settings. The sender creates a generated PulseAudio/PipeWire null sink, selects it as default, moves existing playback streams, and transmits its monitor as 48 kHz stereo s16le PCM (20 ms packets, approximately 1.54 Mbit/s before transport overhead). The receiver plays to its local default output captured at session start. Select a local output or Stop sharing to restore local playback. Either computer can act as sender. Simultaneous reciprocal forwarding is rejected to prevent feedback; stop one route before reversing direction. This is live playback, not synchronized multiroom audio.

All four permissions default off for existing and newly paired computers. Every incoming screen/control/audio/handoff session requires local approval in addition to the persistent permission. Screen/control adds the portal's own consent and stop indicator. Permission changes, pause, lock and revocation cancel active operations. Input queues are bounded; overflow terminates control instead of discarding key releases. Frame storage retains only the latest frame. Audio buffers and live sessions are not persisted.

Audio routing has a private recovery marker. A normal stop restores the old default and moves streams back before unloading the sink. After a daemon crash, the next daemon start attempts to restore and remove its recorded sink; if the audio server is unavailable, cleanup is retried before starting another route. A manually changed local default is preserved during normal cleanup. The source and receiver have independent limits and liveness deadlines.

## Build and install on both machines

On Arch/DaisyOS, build dependencies for the helper are `gcc`, `pkgconf`, `glib2`, `gstreamer`, `gst-plugins-base`, and `libei`. Runtime media dependencies are `gst-plugin-pipewire`, `gst-plugins-base`, `gst-plugins-good`, and `libpulse` (provides `pactl` and `pacat`), plus the existing functioning DaisyOS desktop portals and PipeWire/PulseAudio session. Package versions follow the OS package repository; no additional managed package was introduced.

```bash
dotnet build DaisyOS.sln
DAISYOS_APP_CONFIGURATION=Debug scripts/install-system-apps.sh
scripts/build-continuity.sh
scripts/install-continuity.sh
systemctl --user daemon-reload
systemctl --user restart daisyos-continuity.service
```

Restart the native DaisyOS session as in [TWO_DEVICE_TEST.md](TWO_DEVICE_TEST.md) to load the updated Quick Settings and portal code. The daemon build now also builds and installs `daisyos-continuity-media` beside the daemon. Release publishing includes it in the Continuity payload. Existing pairing identities are preserved. New features check peer Hello capabilities, so an older peer must be updated before using them.

## Acceptance checklist

1. Keep existing clipboard/file tests green. Confirm the four new permissions start off, including for an existing pairing.
2. View each computer from the other. Deny the Continuity prompt and the portal picker separately; neither may send screen frames. Select each monitor on a multi-monitor machine. Verify the chosen screen and pointer positions match.
3. Control a text editor on each computer: type Finnish/English characters, use Shift/Ctrl shortcuts, click/drag, right-click, and scroll. Check source/target layouts intentionally. Stop with Ctrl+Alt+Escape and the target's portal button. Hold Shift or a mouse button while disconnecting; no key/button may remain held. Deactivate the viewer and verify control stops.
4. During viewing/control, lock either computer, pause Continuity, revoke input/screen permission, then forget the device. Each action must stop access. Re-enabling or reconnecting must require new session consent.
5. Hand off plain and rich Notes documents in both directions. Confirm text/title, separate local files and original preservation. Decline once; no note should open. An oversized note should produce feedback. Rich images/formatting are deliberately excluded.
6. Play music locally, select the laptop in Quick Settings, approve on the laptop, and verify existing playback moves there. Start another player; it should follow the selected default. Confirm the remote row uses the Continuity icon. Select local speakers and verify playback returns. Repeat in the opposite direction after stopping the first route.
7. During audio, revoke permission, lock, pause, suspend or disconnect Wi-Fi. Confirm playback/forwarding stops and local routing recovers. Restart the daemon during an active route and verify the generated sink is removed on restart. Change to a different local speaker output while forwarding and verify it stays selected.
8. Measure audio latency/dropouts and screen throughput while sending a large file over Wi-Fi/Ethernet. Current video is a conservative software JPEG preview; hardware video codecs, audio compression and multiroom timing are not implemented.

## Verification and technical references

The backend suite includes real TLS audio transfer with isolated simulated audio tools, permission-default/denial tests, bounded input/JPEG validation and existing pairing/clipboard/file regressions. `scripts/test-continuity-media.py` uses a private mock portal to verify native denial and session cleanup; it does not certify real KWin capture or injection. The full solution and helper must compile, but compositor behavior and playback quality require the checklist above.

Implementation follows the public [RemoteDesktop portal](https://flatpak.github.io/xdg-desktop-portal/docs/doc-org.freedesktop.portal.RemoteDesktop.html), [ScreenCast portal](https://flatpak.github.io/xdg-desktop-portal/docs/doc-org.freedesktop.portal.ScreenCast.html) and [libei API](https://libinput.pages.freedesktop.org/libei/api/index.html). It uses the repository's existing native portal consent backend. Protocol additions are in `protocol/continuity/v1/continuity.proto`, minor revision 1, with feature capability checks and independently bounded TLS connections.
