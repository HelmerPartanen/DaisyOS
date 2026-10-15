# Gaming overlay FPS source — 2026-10-15

The dash was an unavailable reading: the original implementation required an explicitly configured live CSV directory, and the machine did not have MangoHud installed. CPU and memory were independent of this missing FPS source.

The overlay now discovers the shared launcher telemetry directory by default. `daisyos-game` starts hidden MangoHud logging through its native control socket, once per second. It supports a normal system MangoHud installation or an optional user-local runtime. Existing `MANGOHUD_CONFIG` environment options are preserved; file-based MangoHud configuration is disabled for this wrapper to avoid its repeated control-socket initialization.

The live test found two MangoHud v0.8.4 startup behaviors: `no_display` prevents automatic logging from starting until logging is already active; combining `read_cfg` and an environment control socket initializes the same abstract socket twice, leaving the final control descriptor unavailable. The wrapper uses a single configuration pass and a bounded startup helper that waits for the control greeting, sends `:logging=1;`, and exits. It does not simulate game keys or show a second HUD.

## Installed locally

Official Arch `mangohud` and `lib32-mangohud` v0.8.4-1 archives and the missing `lib32-libxkbcommon` v1.13.2-1 dependency were downloaded from the mirror returned by the machine's configured pacman repositories. Archive signatures verified against the installed Arch keyring before extraction. Runtime libraries and licenses are under `~/.local/share/DaisyOS/game-telemetry`; the wrapper is `~/.local/bin/daisyos-game`. System packages and the package database were not modified.

The reusable runtime installer takes repeated `--package ARCHIVE SIGNATURE` arguments, verifies the signatures, accepts only the named runtime packages, and relocates Vulkan manifests. Both 64-bit and 32-bit library dependency checks completed without missing libraries after the dependency was added. A 32-bit game was not launched.

## Verified

- Eleven telemetry parser/directory tests passed.
- Two launcher tests passed, including the hidden logging handshake, argument preservation, and environment option preservation. These tests require permission to use abstract Unix sockets; the default sandbox blocks that operation.
- ShellCheck, Bash syntax, Python compilation, and working-tree whitespace checks passed.
- A native Wayland Vulkan `vkcube` probe generated real frame samples through the installed wrapper; the reloaded overlay visibly displayed **177 FPS**. This validates the complete launcher-to-overlay path, not a particular game's FPS or Proton sandbox compatibility.
- Test render windows closed afterward. Freshness expiry leaves FPS unavailable when no telemetry game is running.

Steam was running and its local configuration was left unchanged. To enable telemetry for a Steam game, set its Launch Options to `/home/helmeri/.local/bin/daisyos-game %command%` and relaunch it. A running game cannot acquire this startup telemetry path merely by opening the hardware monitor. Flatpak Steam is outside the verified host-runtime path.
