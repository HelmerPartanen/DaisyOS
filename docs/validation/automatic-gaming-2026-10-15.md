# Automatic gaming integration — 2026-10-15

## Default user path

For the bundled native game-launcher paths, a user starts the game normally and presses Meta + R. There is no per-game Steam launch option, telemetry installation, environment configuration, logging hotkey, or governor-group setup required by the shipped image.

- The actual archiso package list and default desktop package profile now include GameMode and MangoHud for native and 32-bit applications. The ISO package repositories enable multilib.
- Both production and native-development sessions load the shared gaming environment before exporting activation variables to D-Bus/systemd services. Native Vulkan renderers inherit an inactive capture layer with a per-process control socket.
- The bundled native Steam and Lutris desktop entries and command launchers preload the OpenGL shim only into their application trees. The Wine command wrappers use the same automatic launcher. The Lutris override uses its upstream `net.lutris.Lutris.desktop` identity and preserves its URI MIME types.
- The shell starts hidden capture for the preferred renderer when the hardware monitor opens and stops it when the monitor closes. Connection errors are bounded and retried. This uses existing background polling, not synchronous UI work.
- A package-owned polkit rule grants only the CPU governor helper to active local sessions. It does not grant GPU clock/core-management helpers or arbitrary privileged commands. The live-user setup also ensures the installed GameMode group exists and adds the live account to it.
- ISO staging, native-development installation, and published release payloads include the helpers, launchers and policy. The shell package depends on the telemetry libraries and installs the staged gaming root. A build guard rejects a staged shell assembly without the automatic capture controller.
- Git ignores no longer hide the release definitions, gaming policy, native session entry, or ISO startup scripts.

## Verification

- Sixteen controller, FPS directory/parser and NVIDIA sampler tests passed. The controller tests cover automatic start/stop and retry after an unavailable endpoint.
- Two headless GameMode worker-thread and clock lifecycle tests passed.
- Three clean-root integration tests passed: files/permissions/policy staging, default Vulkan environment with no global OpenGL preload, and OpenGL preload inheritance in a store tree.
- Session supervisor and native-development installer/control tests passed.
- ISO profile, normal Arch packaging, ShellCheck on the gaming helpers, and working-tree whitespace checks passed.
- A normal native Wayland Vulkan probe inherited the new session environment and displayed **182 FPS** in the live hardware monitor without `daisyos-game`, logging keys, or per-game options.
- An OpenGL `glxgears` probe launched through the automatic store-tree environment on KWin's verified `:1` XWayland display showed **181 FPS** in the monitor. Its own five-second reports were approximately 174–180 FPS. These are functional telemetry checks, not game-performance benchmarks.
- A further live lifecycle check verified logging stopped while the test renderer remained running. The exact test renderer was terminated afterward and the monitor's visible state was restored.

Local screenshots and test output were captured under `/tmp/automatic-game-*`; the lifecycle result is retained at `artifacts/automatic-gaming-2026-10-15/capture-lifecycle.json`.

## Shipping boundaries

No ISO was built or installed in this pass. The currently staged published shell predates automatic capture; the new guard rejects it until the production shell is published and copied again. The source integration is tested in the current native session and isolated staging roots.

The installer in this checkout still instantiates `MockInstallerBackend`. Therefore a real installed-user/first-login flow has not been validated and the image must not be advertised as installation-ready on this evidence.

The existing release packaging check remains blocked by the redistribution-license decision, `SKIP` source checksums, and the example release repository address. Normal structural packaging checks pass; the stricter release check correctly rejects publication.

A real Steam/Proton game under a fresh ISO session, 32-bit rendering, Flatpak Steam/Lutris sandbox extensions, and anti-cheat-dependent titles are not certified by these native renderer probes. The bundled default is native Steam/Lutris; Flatpak game-launcher integration must be completed separately before claiming automatic FPS support there.
