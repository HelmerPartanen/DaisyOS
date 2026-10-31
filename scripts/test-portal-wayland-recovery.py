#!/usr/bin/env python3
"""Exercise portal launchers against private sockets; no live session is changed."""
import os
import pathlib
import socket
import subprocess
import tempfile

repo = pathlib.Path(__file__).resolve().parents[1]
with tempfile.TemporaryDirectory(prefix='daisy-portal-display-') as folder:
    root = pathlib.Path(folder)
    runtime = root / 'runtime'
    record = runtime / 'daisyos-native-development/wayland-display'
    record.parent.mkdir(parents=True)
    config = root / 'config'
    config.mkdir()
    binary = root / 'fake-shell'
    binary.write_text('#!/bin/sh\nprintf "%s\\n" "$WAYLAND_DISPLAY"\n')
    binary.chmod(0o700)
    env = dict(os.environ, XDG_RUNTIME_DIR=str(runtime), XDG_CONFIG_HOME=str(config),
               XDG_CURRENT_DESKTOP='DaisyOS', DAISYOS_DESKTOP_SERVICES_BINARY=str(binary))
    launcher = repo / 'scripts/daisyos-desktop-services'
    # Test the frontend's same resolver without starting the real public portal.
    frontend = root / 'frontend'
    frontend.write_text((repo / 'scripts/daisyos-portal-frontend').read_text().replace(
        'exec /usr/lib/xdg-desktop-portal "$@"', 'printf "%s\\n" "$WAYLAND_DISPLAY"'))
    frontend.chmod(0o700)
    with socket.socket(socket.AF_UNIX) as current, socket.socket(socket.AF_UNIX) as other:
        current.bind(str(runtime / 'current')); current.listen(16)
        other.bind(str(runtime / 'other')); other.listen(16)
        record.write_text('current\n')
        for script in (launcher, frontend):
            for inherited, expected in [('missing', 'current'), ('other', 'other'), ('', 'current')]:
                result = subprocess.run([str(script)], env=dict(env, WAYLAND_DISPLAY=inherited), capture_output=True, text=True, timeout=5)
                assert result.returncode == 0, result.stderr
                assert result.stdout.strip() == expected, result.stdout
            record.write_text('missing-too\n')
            result = subprocess.run([str(script)], env=dict(env, WAYLAND_DISPLAY='missing'), capture_output=True, text=True, timeout=5)
            assert result.returncode != 0 and 'reachable Wayland display' in result.stderr
            record.write_text('current\n')
print('PASS: both portal launchers recover a stale display, retain a live display, and fail promptly when neither is reachable')
