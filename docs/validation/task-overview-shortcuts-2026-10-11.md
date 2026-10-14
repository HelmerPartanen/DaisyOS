# Launcher and Task Overview shortcuts — 2026-10-11

## Diagnosis

The live KGlobalAccel registry assigned bare Meta to DaisyOS Task Overview, but `loadedEffects` did not contain that effect. KWin's journal showed repeated v14 QML load failures: `ScreenView` was unavailable because `BentoLayout.js` could not be loaded. The installed dependency existed by the time of this investigation; reusing the failed component URL had not recovered the running effect. Installing v15 at a fresh URL loaded it successfully without restarting KWin.

Meta+Tab was already owned by KWin's `Walk Through Windows` action, alongside Alt+Tab. Its competing binding is removed during live migration while other live switcher bindings are retained. New native sessions explicitly use Alt+Tab for that action.

## Changes

- Bare Meta toggles Launcher through the persistent KWin script and `RequestShellAction("launcher")`, independently of overview QML initialization.
- Meta+Tab toggles the compositor-owned Task Overview. The former Alt+F1 overview alias is removed.
- Package installation checks `loadedEffects` and fails visibly if QML loading fails. Shortcut migration errors are reported rather than silently suppressed.
- v15 identity is consistent in effect metadata, installer/session defaults, compositor command paths and theme configuration; legacy effects are disabled and unloaded.

## Verification

- Native development rebuild/restart completed with zero build errors. KWin and open applications remained running.
- Bridge/overview package checks, native-session installer checks, shell syntax checks and `git diff --check` passed.
- Latest installation reported success and `loadedEffects` contained `daisyos-task-overview-v15`.
- Live registry: Meta owns only `DaisyOS Launcher`; Meta+Tab owns only `DaisyOS Task Overview`; Alt+Tab remains `Walk Through Windows`.
- After shell reload, a temporary Linux uinput keyboard exercised three repeated sequences of left Meta twice, right Meta twice, Meta+Tab and Escape. D-Bus monitoring captured exactly 12 launcher actions. KWin logged exactly three overview activations, entries and exits. Releasing Meta after Meta+Tab produced no additional launcher action. No QML load failure appeared during this check.
- Strict compositor health check: zero required failures and zero warnings.

The injected keyboard exercises KWin's native input route. Physical keyboard and visual acceptance remain to be confirmed by the user. The complete repository test suite was not run for this focused shortcut change.

## Shortcut references

Windows uses Windows+Tab for Task View and the Windows key for Start: https://support.microsoft.com/en-us/windows/keyboard-shortcuts-in-windows-dcc61a57-8ff0-cffe-9796-cb9706c75eec

macOS uses Control+Up Arrow for Mission Control: https://support.apple.com/en-gb/guide/mac-help/mh35798/26/mac/26
