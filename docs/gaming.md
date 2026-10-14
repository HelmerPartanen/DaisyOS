# Gaming

DaisyOS includes desktop-first gaming integration with hardware detection, tool discovery, and a Gaming Mode that can quiet non-critical notification popups without changing the user's normal Do Not Disturb preference.

## Gaming Mode

Toggle **Gaming Mode** on the Gaming page in Settings. When enabled:

- Non-critical notification toast popups are suppressed while you play when **Silence notifications** is enabled.
- A `do_not_disturb_on` icon appears in the system bar with a `sports_esports` badge when Gaming Mode is the reason.
- Turning Gaming Mode off returns to the user's normal notification behavior; the separate Do Not Disturb preference is not changed.

The game-controller button in the system bar provides the same Gaming Mode switch
and a separate choice for silencing non-critical popups. Advanced options remain
on the Gaming page so the system bar stays compact.

## Console mode

Console mode is an opt-in, controller-first home for installed games. It never replaces the desktop merely because a controller is connected: open the Gaming tile's detail action in Quick Settings, or press the controller Guide/PlayStation button while DaisyOS is active. Pressing **B** returns through the current console surface, **X** opens Quick Settings, and **LB/RB** move between Home and Library. In console Quick Settings, use the D-pad to move, **A** to change the selected control, and **Y** for its available detail list (Wi-Fi networks or output devices). Select **Desktop mode** to leave the mode.

Console Quick Settings uses the same Wi-Fi, Bluetooth, Focus, Gaming, volume, and output-device controls as the desktop System Bar. It only shows games discovered on the system; placeholder game cards are never presented as launchable.

## GPU & Vulkan Detection

The `LinuxGamingService` reads `lspci -k` to detect the GPU vendor (AMD / Intel / NVIDIA) and model. It also runs `vulkaninfo --summary` to check for Vulkan support and extract the driver name. The diagnostics separately check for a 32-bit Vulkan loader, because many Windows games still need 32-bit graphics libraries even on a 64-bit system.

## Game Tool Detection

The following tools are detected via `which`:

| Tool        | Description                                    |
|-------------|------------------------------------------------|
| **Steam**   | Game storefront and launcher                   |
| **GameMode**| System performance optimization while gaming   |
| **MangoHud**| FPS overlay for games                          |
| **Gamescope**| Fullscreen game isolation and upscaling       |
| **Wine**    | Compatibility support for Windows software      |

## Controllers and diagnostics

The Gaming page checks whether common controller access rules are installed and whether the Bluetooth service is available and running. These checks do not scan paired-device names or personal files. Wired controllers can still work when Bluetooth is unavailable.

The collapsed **Advanced diagnostics** section presents the same results as a plain-language report that can be copied for support. Raw command output and internal errors are not shown in the normal Settings view.

Missing tools are reported as optional capabilities, not fatal errors. Diagnostics never install packages, enable services, change drivers, or modify the package database.

## Gaming package profile

`os/packages/desktop-gaming.txt` extends the normal desktop profile with Steam, Vulkan diagnostics, 32-bit Mesa and Vulkan support, GameMode, Gamescope, MangoHud, Wine, Winetricks, and Lutris. It is optional and requires Arch's `multilib` repository.

Controller access rules should come from a reviewed `game-devices-udev` package before the gaming image ships. Heroic Games Launcher is also planned, but it must use a reviewed package source rather than silently enabling an untrusted third-party repository. Neither package is added to the image until that packaging path is approved.

Steam launch-option examples:

- GameMode: `gamemoderun %command%`
- MangoHud: `mangohud %command%`
- Both: `mangohud gamemoderun %command%`

Proton is managed through Steam. Start with the version Steam selects for the game; Proton Experimental is an opt-in fallback for newer fixes. Compatibility varies by title, especially for games with anti-cheat systems.

## Services

| Interface          | Implementation       | Mock              |
|--------------------|----------------------|-------------------|
| `IGamingService`   | `LinuxGamingService` | `MockGamingService` |

## Future Work

- Automatic Gaming Mode: detect when a game is running and toggle Gaming Mode on/off.
- Per-game profiles (launch options, GPU settings, performance profiles).
- Game library integration with Steam and other launchers.
