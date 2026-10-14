# DaisyOS Live Session Startup

## Startup sequence

The Xorg live image uses one authoritative path:

```text
getty tty1 auto-login
  -> daisyos user's .bash_profile
    -> startx
      -> .xinitrc
        -> /usr/local/bin/daisyos-session
          -> /opt/DaisyOS/shell/DaisyOS.Shell --real-services --production
```

The optional system-level `os/systemd/DaisyOS-shell.service` uses the same
session supervisor. It is not enabled in the live image because enabling both it
and tty auto-login would create competing X servers.

Production mode disables development-only `Ctrl+Q` exit and `Alt+R` resource
diagnostics shortcuts. First-run onboarding remains available because production
mode is not the same as a configured user profile.

## Crash handling

`daisyos-session` retries an unexpected shell exit at most three times with a
two-second delay. A normal zero-status exit ends the X session and is not
treated as a crash. Output is written to both:

- `~/.local/state/DaisyOS/session.log`
- the journal under identifiers `daisyos-session` and `daisyos-shell`

Every unexpected exit also records the attempt number, exit status, signal,
firmware mode, renderer, and kernel in the session log. Production live
sessions enable bounded .NET minidumps and core dumps so a native crash leaves
actionable evidence instead of only a generic exit code.

After three failures, the supervisor writes
`~/.local/state/DaisyOS/use-fallback-session` and starts the fallback terminal.
The marker persists across X restarts, preventing an auto-login crash loop.

## Recovery session

The fallback is a plain xterm with short, non-technical instructions. It does
not claim the desktop started successfully and does not delete user files.

Useful diagnostics:

```bash
less ~/.local/state/DaisyOS/session.log
journalctl -t daisyos-session -t daisyos-shell
ls -l /opt/DaisyOS/shell/DaisyOS.Shell
```

After correcting the problem:

```bash
sudo /usr/local/bin/daisyos-recover-session
```

Close the recovery terminal. Tty auto-login starts X again, and the supervisor
will retry the normal shell because the recovery marker has been cleared.

## Failure boundaries

- The supervisor never retries indefinitely.
- It never starts a second shell while one is running.
- Missing binaries go directly to recovery.
- Recovery is intentionally a developer/live-media tool, not the future
  end-user repair environment.
- The Wayland/KWin production session will retain the same bounded-restart and
  fallback principles but must use compositor-native session ownership.

## Repository validation

`scripts/test-session-scripts.sh` uses temporary fake shell and fallback
executables. It verifies normal exit, exactly three crash attempts, persistent
recovery selection, diagnostic recording, and fallback execution without
starting X or changing the host system.
