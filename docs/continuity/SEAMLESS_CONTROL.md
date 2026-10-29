# Keyboard, mouse and audio between computers

Pair the computers once in **Settings → Continuity**. Keep both computers unlocked and update both to the same revision. No separate Connect action is needed before using a feature.

## Move the pointer between screens

1. On the computer whose keyboard/mouse you want to use, find the paired computer in Continuity.
2. Choose where it sits: **On my left**, **On my right**, **Above me**, or **Below me**. Click **Link keyboard and mouse**.
3. Approve the system control request on the receiving computer and input capture on the source computer. The link then shows **Ready**.

Move the pointer across the chosen edge. Keyboard input follows it onto the other computer. Move back through the entry edge to return. There is no viewer window and no screen stream is needed. Approvals apply to the link session, not each crossing.

**Ctrl+Alt+Shift+Escape** returns control locally. The system permission indicator and Continuity both have a stop control. Lock, pause, revocation or a lost connection stops the link; reconnecting requires fresh system consent. System capture-zone revocation and destination input-device/layout changes stop the link safely; relink after changing monitor layout. A second computer's own keyboard/mouse can be linked in the reverse direction in the same way. Reverse local capture is suspended while that computer is receiving remote input to prevent echo loops.

The side selection is remembered. Input uses the destination's keyboard layout. The link supports normal keyboard input, mouse buttons and scrolling; it does not move application processes or drag files between computers. Source monitors on the outside-most boundary for the selected side receive barriers; the destination maps motion over its available outputs. Physical multi-monitor arrangements and both computers’ recovery behavior still need real two-device acceptance.

## Use another computer as speakers

Open **Quick Settings → Audio outputs**, then select the paired computer with the Continuity icon. The receiving computer gets an **Allow / Decline** notification. Allowing speaker access remembers that grant for this paired computer, so future selections connect directly while it is unlocked. The sender does not need to enable an unrelated receiving permission first.

Existing playback moves to the selected output. You can select the output before starting music: the route remains connected while idle. Select local speakers to return playback, or Disconnect in Continuity. Either computer can send audio; simultaneous forwarding in both directions is rejected to prevent a feedback loop. The output list updates approval/connection status automatically. Turn off **Audio playback** under the paired computer's **Access to this computer** to revoke remembered access and block further requests until re-enabled.

## Screen sharing and Notes

**View screen** opens a viewer and asks the other computer to choose a screen in the system picker. A single available screen is selected automatically; the user still explicitly chooses Share. The viewer explains where approval is needed instead of showing an unexplained black window.

In Notes, **Continue on another computer…** sends directly when only one computer is paired. The receiving computer approves the first handoff in the notification; later handoffs open directly until App handoff access is revoked. Handoff sends a text snapshot, preserves the original and confirms restoration by waiting for Notes to save the received document. Old running Notes versions that cannot consume the activity produce an update/reopen notification instead of reporting a successful handoff.

## Install/update

```bash
scripts/build-continuity.sh
scripts/install-continuity.sh
systemctl --user daemon-reload
```

The daemon and native helper are installed together into immutable release directories. Updating stops an active user daemon, switches the complete payload, and restarts it. This avoids corrupting running .NET assemblies with an in-place copy. It preserves pairing state. If the daemon was stopped, turn Continuity on in Settings. Reload the native shell/standalone apps after building them to use the new controls; don't replace a running app's assemblies manually. In a native development session, when ready to reload the UI (the shell was deliberately not restarted during concurrent launcher work):

```bash
scripts/daisyos-native-development restart
systemctl --user restart daisyos-desktop-services.service
```

The restart command builds the configured shell and standalone apps, and preserves KWin and open applications. The desktop-services restart loads the non-focusing stop indicators and portal-picker changes.

Build the portal capture helper with `scripts/build-portal-capture.sh` before rebuilding native UI/desktop services, so InputCapture uses the corrected logical output geometry.

Build requirements for the native helper include `gio-unix-2.0`, `gstreamer-app-1.0`, `libei-1.0`, and `xkbcommon`. Runtime audio uses PipeWire/PulseAudio with `pactl` and `pacat`. The existing InputCapture and RemoteDesktop portals must be available; the laptop's current session reports both. The helper and protocol advertise a separate EDGE_INPUT capability so an older daemon is rejected clearly rather than pretending to support the link.

## What was actually tested

- Actual laptop output geometry at 125% scaling: the old helper incorrectly reported 960 × 540; xdg-output correctly reports 1536 × 864, matching KWin.
- Real libei capture of keys/motion (including held Ctrl), real libei key/button/pointer injection, return-edge messages and emulation release against isolated real libeis servers.
- InputCapture request/barrier/activation/release/session lifecycle on a private bus, including the inclusive-pixel barrier endpoints required by the portal specification.
- All four directions and proportional return positions in the production native geometry engine; authenticated TLS/helper-pipe round trips and revocation across simulated peers.
- Real PCM capture → TLS → real PipeWire/PulseAudio playback → real output monitor, including idle-before-playback and output restoration. Temporary virtual outputs keep the test silent and preserve the user's actual selected output.
- Consent denials, remembered-grant revocation, UI actions/state, and immutable daemon upgrades.

These tests do not replace checking actual cursor movement between the desktop and laptop, physical speaker playback, latency, suspend and multi-monitor behavior. The desktop is currently unavailable; those acceptance checks remain pending. This is not a claim of Apple-level production polish or completed two-device certification.

Technical contracts: [InputCapture portal](https://flatpak.github.io/xdg-desktop-portal/docs/doc-org.freedesktop.portal.InputCapture.html), [libei/libeis](https://libinput.pages.freedesktop.org/libei/api/index.html).
