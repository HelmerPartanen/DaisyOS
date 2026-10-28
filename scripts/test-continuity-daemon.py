#!/usr/bin/env python3
"""Run under dbus-run-session: isolated real daemon IPC/start/stop, sharing off."""
import json
import pathlib
import signal
import subprocess
import tempfile
import sys
import time

ROOT = pathlib.Path(__file__).resolve().parents[1]
BINARY = pathlib.Path(sys.argv[1]).resolve() if len(sys.argv) > 1 else ROOT / 'src/DaisyOS.Continuity/bin/Debug/net10.0/DaisyOS.Continuity'
with tempfile.TemporaryDirectory(prefix='daisy-continuity-ipc-') as temporary:
    log = pathlib.Path(temporary) / 'daemon.log'
    with log.open('w') as output:
        daemon = subprocess.Popen([str(BINARY), '--state-dir', str(pathlib.Path(temporary) / 'state'), '--port', '0'], stdout=output, stderr=output)
        try:
            deadline = time.monotonic() + 15
            while time.monotonic() < deadline:
                if daemon.poll() is not None:
                    raise AssertionError('Daemon exited: ' + log.read_text())
                result = subprocess.run([str(BINARY), '--status'], capture_output=True, text=True, timeout=12)
                if result.returncode == 0:
                    status = json.loads(result.stdout)
                    assert status['enabled'] is False
                    assert status['visible'] is False
                    assert status['sessionReady'] is False
                    assert all(not device['trusted'] for device in status['devices'])
                    assert len(status['deviceId']) == 64
                    break
                time.sleep(0.1)
            else:
                raise AssertionError('IPC did not become ready: ' + log.read_text())
            daemon.send_signal(signal.SIGTERM)
            assert daemon.wait(timeout=20) == 0, log.read_text()
            print('PASS: real daemon starts, serves versioned D-Bus status with sharing disabled, and shuts down on SIGTERM.')
        finally:
            if daemon.poll() is None:
                daemon.kill()
                daemon.wait()
