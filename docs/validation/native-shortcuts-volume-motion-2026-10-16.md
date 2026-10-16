# Native shortcuts, hardware volume and spring motion — 2026-10-16

DaisyOS now explicitly migrates both persistent shortcut configuration and live KGlobalAccel grabs. Registering a DaisyOS shortcut alone was insufficient: the live registry still assigned Meta+E to Dolphin as well. The installer reads the first-party bridge registrations, clears competing desktop UI registrations, assigns each managed key to DaisyOS, and verifies ownership. The previous shortcut configuration is backed up once. KWin's ordinary window management and hardware/accessibility backends retain their own responsibilities.

Hardware output volume, fine volume steps and mute, plus microphone mute, go through the existing PipeWire audio service. Requests run serially; adjacent volume repeats combine while backend reads are pending. Actual backend values are read back before displaying the result. The indicator uses QuickSettingsSlider with ShowThumb disabled, the taskbar's three-layer material resources, a rounded KWin blur region that follows motion, and a passive layer surface with no pointer or keyboard ownership. Its placement clears the taskbar. A new key refreshes its 1.5-second timeout and can reverse an active dismissal. Visible content is measured before its first native map; without that preparation the compositor surface could be mapped while the indicator remained unpainted.

Shared shell slides use an exact critically damped spring. They retain position and velocity during reversals and settle by position and velocity thresholds instead of cutting off at a fixed deadline. Taskbar/System Bar reveals, Launcher and system flyouts share this implementation. Other positional/size transitions use a shared spring resource. Lock-screen presence and the console carousel also use the spring solver. Overview geometry and taskbar-preview scale use Qt spring animations. Reduced motion settles shell movement immediately and disables overview spring behavior.

## Verification

- Final Shell build: zero warnings/errors. Standalone Files, Notes, Settings and Disk Manager builds and installation also passed during the controlled native-session update.
- Focused .NET suite: 24 passed, covering volume repeats/mute/clamping, actual indicator renders in both themes, spring refresh-rate independence, interrupted visibility, reduced motion, bottom chrome, lock-screen layout and console idle work.
- Qt 6 suite on the actual Wayland compositor: 32 passed. This includes spring reversal/settling and rounded preview rendering. The offscreen run's two rounded-render failures did not reproduce on Wayland.
- Shortcut policy fixture, bridge/overview/taskbar-preview package checks and native window-motion fixtures passed. git diff --check passed.
- Live registry: each of the 25 managed shortcuts has exactly one DaisyOS owner. No active shortcuts remained in the competing desktop UI components examined by the policy. Meta+E invoked the standalone DaisyOS Files executable.
- Live volume invocation changed the default sink from 100% to 95%, then restored 100%. The corrected indicator was captured and inspected on the native desktop, including its translucent blurred material. Its first-frame layout reported a 328×160 host and a 280×56 card.
- Native overview open/close reversal settled with only the blur and frame effects active afterward.
- The native shell was reloaded using its supervisor protocol; KWin and ordinary applications remained running.

Physical keyboard presses and exhaustive interaction combinations were not performed. Shortcut registry ownership and invoking the registered compositor actions were verified directly. These checks do not establish a universal guarantee that every animation is flawless.

The separate read-only session health report found that portal monitor capture is currently unavailable (no advertised monitor sources). This pass does not claim screen-sharing acceptance. Evidence is under artifacts/native-input-motion-2026-10-16/.
