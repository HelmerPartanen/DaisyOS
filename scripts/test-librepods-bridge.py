#!/usr/bin/env python3
"""Smoke-test the built companion without touching the real session or radio."""
import json
import os
from pathlib import Path
import signal
import socket
import subprocess
import sys
import tempfile
import time

binary = Path(sys.argv[1]).resolve()
with tempfile.TemporaryDirectory(prefix='pods-smoke-') as temporary:
    root = Path(temporary)
    runtime = root / 'runtime'
    runtime.mkdir(mode=0o700)
    env = dict(os.environ, XDG_RUNTIME_DIR=str(runtime), XDG_CONFIG_HOME=str(root / 'config'),
               XDG_CACHE_HOME=str(root / 'cache'), XDG_DATA_HOME=str(root / 'data'),
               DBUS_SYSTEM_BUS_ADDRESS='unix:path=' + str(root / 'no-system-bus'),
               QT_QPA_PLATFORM='offscreen', QT_QUICK_BACKEND='software')
    bus_config = root / 'bus.conf'
    bus_config.write_text('<busconfig><type>session</type><listen>unix:tmpdir=/tmp</listen>'
        '<policy context="default"><allow send_destination="*"/><allow receive_sender="*"/>'
        '<allow own="*"/></policy></busconfig>')
    env['QT_QPA_PLATFORMTHEME'] = ''
    env['XDG_CURRENT_DESKTOP'] = ''
    with (root / 'companion.log').open('w+') as log:
        process = subprocess.Popen(['dbus-run-session', '--config-file', str(bus_config), '--', str(binary), '--hide'], env=env,
                                   stdout=log, stderr=log, start_new_session=True)
        try:
            endpoint = runtime / 'daisyos-librepods.sock'
            for _ in range(100):
                if endpoint.exists() or process.poll() is not None:
                    break
                time.sleep(.1)
            if not endpoint.exists():
                log.seek(0)
                raise AssertionError(log.read())
            assert endpoint.stat().st_mode & 0o077 == 0, 'socket must be private'
            def request(command):
                with socket.socket(socket.AF_UNIX) as client:
                    client.settimeout(3)
                    client.connect(str(endpoint))
                    client.sendall(command.encode() + b'\n')
                    return client.makefile('r').readline().strip()
            status = json.loads(request('status'))
            assert status['version'] == 1
            assert not status['connected']
            assert status['mode'] == -1
            assert status['modes'] == []
            assert status['nearbyAddress'] == ''
            assert request('noise:AA:BB:CC:DD:EE:FF:1') == 'error'
            assert request('noise:AA:BB:CC:DD:EE:FF:9') == 'error'
            assert request('unknown') == 'error'
            print('LibrePods bridge: private socket, status, unavailable hardware and command rejection passed.')
        finally:
            if process.poll() is None:
                os.killpg(process.pid, signal.SIGTERM)
            process.wait(timeout=10)
