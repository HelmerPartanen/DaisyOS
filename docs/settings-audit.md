# Settings product and backend audit

This document is the implementation contract for the DaisyOS Settings app. A control may only appear when its current state can be read from its authoritative source and a failed change can be reported to the user.

## Supported journeys

| Page | Authoritative source | Behavior |
|---|---|---|
| System | `powerprofilesctl`, `/sys/class/power_supply`, DaisyOS session state | Live power profile, battery status when present, and system-bar date |
| Displays | `kscreen-doctor`, with XRandR fallback | Detect active outputs, choose main display, resolution, scale, refresh rate, and KWin Night Light |
| Sound | PipeWire through `wpctl` | Output/input devices, volume, microphone mute, per-app volume, and test sound |
| Network | NetworkManager through `nmcli` | Live connection status, Wi-Fi radio, discovery, secure connection, and disconnect |
| Bluetooth | BlueZ through `bluetoothctl` | Adapter state, discovery, connect/disconnect, and forget |
| Appearance | DaisyOS appearance and wallpaper services | Theme, accent, wallpaper, high contrast, and reduced motion |
| Notifications | DaisyOS notification session state | Do Not Disturb |
| Apps | XDG MIME associations through `xdg-mime` | Default browser, file manager, and text editor |
| Storage | Mounted Linux filesystems | Live used and free capacity |
| Keyboard and mouse | `localectl`, `kcminputrc`, and KWin reconfigure | Keyboard layout, pointer speed, natural scrolling, and tap-to-click |
| Date and time | `timedatectl` | Automatic time, time zone, and safeguarded manual time |
| About | `/etc/os-release`, procfs, runtime data, and `pacman -Qu` | OS/hardware/session details and live update status |

Hardware-dependent settings are omitted or replaced with a clear unavailable message when the corresponding service or device is absent. Backend-backed pages refresh while open so changes made by hardware or other tools are reflected without restarting Settings.

## Safety guarantees

- Resolution, scale, and refresh-rate changes require confirmation and revert automatically after 15 seconds.
- Failed backend operations show human-readable feedback and re-read the authoritative state.
- Wi-Fi passwords are handed directly to NetworkManager and are not persisted by DaisyOS.
- Settings never fabricates a display, audio device, Bluetooth adapter, battery, or network.
- Old private preference values never override live Linux state.

## Removed placeholders

The redesign removed controls that previously stored a value without implementing the promised system behavior: automatic updates, metered-update policy, app background policy, external-link prompts, recent-app policy, trash retention, low-space warnings, removable-drive policy, privacy permission policy, diagnostics sharing, account and guest-session policy, keyboard repeat/compose remapping, regional week format, speculative spatial audio, notification banner/sound policy, and gaming automation.

These features may return only with a real backend, state read-back, failure handling, persistence tests, and a complete UI journey.
