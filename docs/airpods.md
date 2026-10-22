# AirPods integration

DaisyOS uses the native LibrePods Linux Qt companion from
https://github.com/librepods-org/librepods, pinned in
`packaging/librepods/revision.txt`. The upstream Rust rewrite currently does not
expose the control/status API used here. Do not substitute a nightly binary.

The shell supplies the UI; LibrePods owns Apple's Bluetooth protocol, battery
reports, ear detection and model identification. A small GPL-3.0 integration
adds newline-framed protocol v1 on `$XDG_RUNTIME_DIR/daisyos-librepods.sock`.
The socket is user-only. It reports connection, address, name, battery text,
confirmed listening mode and model capabilities. Commands target the connected
address, and the shell waits for the AirPods' own mode acknowledgement.
The bridge never exposes the pairing keys LibrePods stores.

## Build and install on the development laptop

Install build/runtime dependencies on Arch:

```sh
sudo pacman -S --needed bluez bluez-utils qt6-base qt6-declarative qt6-connectivity qt6-tools libpulse openssl cmake git python wireplumber
scripts/build-librepods.sh
sudo scripts/install-airpods.sh --install
```

Log out and back in, or start the service in the current graphical user session:

```sh
systemctl --user daemon-reload
systemctl --user start daisyos-librepods.service
```

Restart the shell with the new build. Pair AirPods once through Bluetooth
settings while their case is in pairing mode. BlueZ trusts successfully paired
devices and its default A2DP reconnect policy reconnects dropped links. The shell
also retries nearby paired AirPods with a two-minute cooldown. Proximity can come
from BlueZ or a fresh LibrePods advertisement authenticated using the pair’s
identity key; an authenticated advertisement must report disconnected AirPods
with the case open or a bud outside the case. It never turns
Bluetooth on automatically, silently pairs unknown devices, or reconnects a
device explicitly disconnected during this shell session. Nearby devices can
produce an actionable “Connect to …?” notification; confirmed connections
produce “Connected to …” using the supplied AirPods icon. Do Not Disturb applies.
LibrePods owns LE discovery; the shell does not run a permanent second scan.

The volume control follows the default PipeWire output. On connection the shell
selects the matching named AirPods sink if PipeWire publishes it; users can
choose another output using the chevron. Listening controls continue to address
the connected AirPods even when another output is selected. Bluetooth devices
remain accessible from the output page while the main Bluetooth tile is hidden.

## Distribution

Publish builds the pinned companion and stages `airpods-root` in the release
payload. ISO staging and the Arch package install it, its user service, BlueZ
service enablement and the WirePlumber AVRCP configuration. The companion's GPL
license, exact revision and modified corresponding source accompany the binary
in `/usr/share/daisyos/librepods/`. Build requires network access to GitHub.

## Verification and limits

Use `systemctl status bluetooth`, `bluetoothctl show`,
`systemctl --user status daisyos-librepods`, and
`journalctl --user -u daisyos-librepods` for diagnostics. A hardware rfkill switch
must be enabled physically. No DeviceID spoofing or hearing-aid modifications
are applied. Existing WirePlumber sessions pick up the AVRCP setting on next
login; do not run `mpris-proxy` alongside it.

Capabilities follow the pinned upstream model table: Pro 2 and AirPods 4 ANC
have adaptive mode; Pro 1 and Max have off/ANC/transparency. Unsupported or
unrecognized models retain audio support but do not expose guessed controls.
Firmware may reject Off or ANC based on ear detection/settings; rejection leaves
the previous confirmed mode selected and shows feedback. Multiple simultaneous
AirPods pairs follow the one pair managed by this upstream implementation.

Before release, test physical pairing, opening the case, manual disconnect,
Bluetooth off/on, suspend/resume, moving out of range, DND, mode changes on the
stem, all supported mode buttons, speaker/output switching and case/earbud
battery updates. Automated socket and UI tests cannot verify radio behavior.
