# Gaming

DaisyOS includes desktop-first gaming integration with hardware detection, tool discovery, and a Gaming Mode that can quiet non-critical notification popups without changing the user's normal Do Not Disturb preference.

## Gaming Mode

Toggle **Gaming Mode** on the Gaming page in Settings. When enabled:

- Non-critical notification toast popups are suppressed while you play when **Silence notifications** is enabled.
- A `do_not_disturb_on` icon appears in the system bar with a `sports_esports` badge when Gaming Mode is the reason.
- Turning Gaming Mode off returns to the user's normal notification behavior; the separate Do Not Disturb preference is not changed.
- When GameMode is installed, the shell holds a GameMode request (`gamemoderun sleep infinity`) for as long as Gaming Mode is on, to activate the system-wide policy. The shell also registers detected Steam/Lutris game processes and their descendants with GameMode so configured per-process priority and I/O tuning apply to the game. Requests are released when Gaming Mode is disabled or the processes disappear. Available tuning depends on the installed GameMode configuration and permissions. Native requests run on a background worker, and the helper uses a parent-death signal (`setpriv`) so a shell crash or forced restart cannot leave its request running forever.

### Built-in GameMode access

The ISO includes GameMode and a narrowly scoped policy that lets the active local desktop session use its CPU governor helper without a password or group setup. The live account also belongs to the installed GameMode group. Optional GPU clock/core-management helpers retain GameMode's normal authorization rules; the default image does not enable overclocking.

For development or older manually assembled systems, `scripts/configure-gaming.sh --check` and `gamemoded -t` remain diagnostics. They are not post-install setup steps for the shipped ISO.

### Automatic Gaming Mode

**Turn on automatically when a game starts** (Settings → Notifications, on by default) checks `/proc` every three seconds for a running Steam game (`reaper SteamLaunch AppId=…`, native or Proton) or a Lutris game (`lutris-wrapper`). Gaming Mode turns on when one starts and off when it exits. Gaming Mode that you switched on yourself is never switched off automatically, and switching it off during a game keeps it off until that game exits.

The game-controller button in the system bar provides the same Gaming Mode switch
and a separate choice for silencing non-critical popups. Advanced options remain
on the Gaming page so the system bar stays compact.

## Proton and non-Steam compatibility

Steam owns Proton Stable, Experimental, and per-game compatibility selection.
Open Settings → Notifications → Gaming → **Open Steam Library**, select the
game, then open Properties → Compatibility. Select a compatibility tool for
that game, or clear **Force the use of a specific compatibility tool** to return
to Steam's default. DaisyOS does not rewrite Steam's compatibility configuration.

Optional Proton-GE builds can be installed per-user by a trusted tool such as
ProtonUp-Qt. Steam discovers local tools in
`~/.steam/root/compatibilitytools.d/` ([Valve's installation instructions](https://github.com/ValveSoftware/Proton#install-proton-locally)); restart Steam if a newly installed tool does not appear. DaisyOS does not redistribute Proton-GE.

For non-Steam Windows games, the gaming profile includes `umu-launcher`. Give
each game its own prefix and ID, for example:

```sh
GAMEID=umu-my-game WINEPREFIX="$HOME/Games/umu/my-game" umu-run "/path/to/game.exe"
```

UMU ([upstream project](https://github.com/Open-Wine-Components/umu-launcher)) may obtain its Proton/runtime components on first use. It runs as the logged
in user and stores the prefix under that user's home. Use the game's publisher
or storefront launcher when it supplies the required environment. Compatibility
is unknown unless that exact game/runtime/driver path has been tested. In
particular, anti-cheat support depends on the publisher and may be unavailable.

The diagnostics report lists Proton tools in common Steam library locations and
checks for standard DXVK and VKD3D-Proton payload directories. Payload presence
does not prove that Direct3D 9/10/11 or Direct3D 12 launches correctly. NTSYNC
status reports kernel/module availability only; DaisyOS does not load the
module or force Proton to use it.

## Fullscreen games

With auto-hide enabled, the taskbar and system bar remain independently available above fullscreen games. Move the pointer to the bottom center for the taskbar or bottom right for the system bar. Each bar keeps its own hide timer. Hidden bars pass input through; the narrow reveal target does not request keyboard focus. Games that lock the pointer must release it before the pointer can reach the screen edge.

## Performance validation

### Compatibility diagnostics

Run `daisyos-gaming-diagnostics` to collect the detected GPU/driver, Vulkan
loader evidence, available Proton tools in common Steam library locations,
UMU presence, and NTSYNC kernel/device status. Use `--json` for a machine
readable report. NTSYNC detection is informational: DaisyOS does not load the
module or force a Proton synchronization mode. Steam remains responsible for
selecting Stable, Experimental, or another installed compatibility tool per
game. The report does not establish that a game or anti-cheat system is
compatible.

The overlay's CPU percentage averages all logical CPUs. A busy game thread can limit FPS while total CPU and GPU utilization remain below 100%. Utilization alone does not identify a bottleneck or prove that the wrong GPU is in use.

Compare the same in-game scene and settings, recording frame times, per-thread CPU use, GPU clocks/power/utilization, and background load. Check the game's frame limit and VSync, active output refresh rate, Vulkan driver/device, and GameMode registration. Repeat with the hardware overlay hidden to measure its cost. Do not force CPU affinity, GPU overclocks, or global driver overrides without measurements.

## Performance overlay

Press **Meta + R** to toggle the top-right FPS / CPU / GPU / MEM pill. It uses the taskbar's theme-aware material and KWin blur on a passive Wayland overlay layer above fullscreen games. It does not take focus or intercept game input. Sampling runs once per second while visible.

CPU is total system utilization and **SYS RAM** is whole-system memory in GiB (total minus MemAvailable), including the game, desktop, Steam, and any open developer tools. It is not game-only memory. GPU utilization comes from supported DRM drivers, NVIDIA’s NVML library, or MangoHud telemetry; unavailable readings show **—**. FPS requires game telemetry, and never reports the shell's frame rate.

FPS capture is built into the shipped desktop session. The image includes native and 32-bit MangoHud libraries, and the bundled Steam/Lutris launchers carry Vulkan and OpenGL telemetry into their games. Users open the monitor with **Meta + R**; they do not edit Steam launch options, install telemetry packages, set environment variables, or press a logging hotkey.

The shell discovers each renderer's per-process control socket and starts hidden capture while the monitor is visible. It prefers the focused renderer and stops capture when the monitor closes. Applications outside a game-launcher tree do not receive a global OpenGL preload. Vulkan applications inherit the session's disabled-by-default capture layer; capture activates only when the monitor is open. Initial FPS can take a few seconds to appear. No active renderer means **—**.

The telemetry folder is selected automatically under the user's runtime directory, with a cache fallback. Explicit directory overrides and `daisyos-game` remain available for developer diagnostics or launching games outside the normal desktop routes, rather than user setup requirements. The signed local-runtime installer supports development machines; the ISO uses distribution packages.

The verified automatic routes are the bundled native Steam/Lutris desktop entries and command launchers, session-inherited native Vulkan applications, and the shipped Wine command wrapper. Flatpak launchers and sandbox-specific telemetry extensions are outside this host-runtime path and need separate integration before being advertised as automatic.

Development installs also wrap native Steam/Lutris desktop entries at the user
level through `install-system-apps.sh`. Existing localized names, arguments, and
desktop actions are retained. An already-running Steam process must exit and
reopen to inherit telemetry; this does not change per-game Steam launch options.

See the [2026-10-15 gaming validation](validation/gaming-2026-10-15.md) for the measured hardware, live interaction checks, and remaining administrator setup.

## Reducing background contention

While Gaming Mode is enabled, desktop live wallpaper playback and its preparation job pause, including for windowed games. Lock-screen wallpaper remains independent. App prefetching stops during Gaming Mode or while a fullscreen application is active, then becomes eligible again after four seconds of shell inactivity. Already-issued kernel readahead cannot be recalled.

Animated desktop clocks stop their render timer while covered or Gaming Mode is active, and refresh immediately when the desktop returns.

The performance overlay samples NVIDIA utilization directly through [NVML](https://docs.nvidia.com/deploy/nvml-api/latest/api/group__nvmlDeviceQueries.html), without launching a GPU monitoring process every second. Missing drivers and device errors show **—** and back off before retrying.

### Measuring stutter

Use the same game scene or replay, graphics settings, resolution, frame cap, and warmed shader cache for each comparison. Stop builds and downloads first. Record at least three equal-duration runs for each configuration; do not compare different map views or average FPS alone.

For `scripts/analyze-game-frametimes.py`, record a MangoHud **per-frame** log (`log_interval=0`). Periodic aggregated logs cannot establish true per-frame 1% lows. A short capture in a directory under `$XDG_RUNTIME_DIR` avoids benchmark writes competing with the game on a hard disk; keep captures bounded and copy the CSV afterward. See [MangoHud configuration](https://github.com/flightlessmango/MangoHud#fps-logging).

```sh
python scripts/analyze-game-frametimes.py /path/to/game.csv --skip-seconds 20 --duration 60
```

The report includes average FPS, p99 and maximum frame time, and 1% low FPS calculated as 1000 divided by the mean of the slowest 1% of frame times in milliseconds. This definition differs from simply taking the first percentile of reported FPS. The interval options require per-frame logs.

These changes reduce specific shell work; they do not establish a measured game 1% low improvement. Include an overlay-hidden run to quantify the overlay’s remaining cost.

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

`os/packages/desktop-gaming-nvidia.txt` is the same profile plus the NVIDIA driver stack (`nvidia-open`, `nvidia-utils`, `lib32-nvidia-utils`) for Turing (RTX 20-series / Quadro RTX) and newer GPUs. `nvidia-utils` enables `nvidia_drm` kernel modesetting, which KWin Wayland requires. Older NVIDIA GPUs need the legacy driver and are not covered by this profile.

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

- Per-game profiles (launch options, GPU settings, performance profiles).
- Game library integration with Steam and other launchers.
