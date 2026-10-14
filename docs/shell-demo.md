# Polished Shell Demonstration

This milestone means DaisyOS can be demonstrated as a cohesive desktop shell in
the supported nested KWin environment without pretending that the project is a
production-ready operating system.

## Acceptance contract

The milestone is complete only when all of the following are true:

- The desktop, Dock, status area, Launcher, Quick Search, Power, Calendar,
  Notifications, Updates, Lock Screen, Settings, and Files render without
  clipping or blank content in their supported demo sizes.
- Only one shell overlay is interactive and visible at a time. `Escape` returns
  to the desktop, and opening one surface directly replaces the previous one.
- Launcher, search, power, notifications, calendar, Settings, Files, and lock
  are reachable through their visible controls or documented keyboard
  shortcuts.
- The locked shell blocks global shortcuts and hides owned windows until the
  user unlocks it.
- The shell renders at 1024x640, 1280x800, and 1600x900. Settings supports a
  640x520 minimum; Files supports a 720x500 minimum with an icon-only sidebar.
- Light/dark appearance, accent color, reduced motion, high contrast, Dock
  position, and volume are real persisted settings rather than preview-only
  controls.
- Missing host commands and unavailable services produce short, calm user
  messages and do not terminate the shell.
- Automated tests, formatting, packaging checks, and the isolated nested-KWin
  runtime gate pass from the same revision.

## Demonstration sequence

1. Start the nested runtime with `scripts/test-kwin-nested-runtime.sh`.
2. Show Launcher, search for an app or setting, and close it with `Escape`.
3. Open Calendar and Notifications from the status area.
4. Open the volume, gaming, and user controls from the status area.
5. Open Settings, change appearance, and minimize/restore it from the Dock.
6. Open Files, navigate using the sidebar and tabs, and minimize/restore it.
7. Open Updates and show its read-only package state.
8. Lock the shell, demonstrate that shell shortcuts are blocked, then unlock.
9. Open Power and show the safe preview-mode explanation.
10. Quit the development session with `Ctrl+Q`.

## Repeatable evidence

Generate repeatable rendered frames and run the interaction assertions with:

```bash
scripts/capture-shell-demo.sh
```

The command writes PNG evidence to `artifacts/shell-demo/current/`. The frames
are a fast regression gate; the nested runtime and the interactive sequence are
still required because headless rendering cannot prove compositor blur, focus,
clipboard, XWayland, or frame pacing.

## Explicitly outside this milestone

This milestone does not certify installation, disk encryption, Secure Boot,
native Wayland client support, broad hardware compatibility, GPU scheduling,
VRR, HDR/color management, or production update installation. Those remain
separate roadmap work and must not be described as complete based on this demo.
