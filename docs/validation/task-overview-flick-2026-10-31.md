# Task overview flick validation — 2026-10-31

The reported failure was an upward flick starting the overview and then snapping
back, with jitter during opening. Reloading the v15 effect after replacing its
QML did not activate newly added diagnostic messages. Loading the same updated
code from a fresh effect package URL did activate them; the user then reported
that the flick felt good.

The production effect is now `daisyos-task-overview-v16`. The running session was
updated to that package, with v15 and the temporary diagnostic package unloaded
and removed. The installed package matches the source tree, contains no temporary
logging, and retains the `DaisyOS Task Overview` shortcut. Existing v15 theme and
motion settings were copied into unset v16 settings. Future installs perform the
same migration and disable/unload the old effect.

Release handling also distinguishes KWin cancellation followed by direction or
axis reselection from a below-distance finger release. Tests cover both that
handoff and short flicks through the real QML handlers and transition state
machine, including delayed preview preparation.

Checks:

- `scripts/test-task-overview-gestures.sh`
- `scripts/test-kwin-window-bridge-package.sh`
- `scripts/test-native-development-session-install.sh`
- Live `loadedEffects` contains v16 and neither v15 nor the diagnostic effect.
- Live shortcut registration still contains `DaisyOS Task Overview`.
