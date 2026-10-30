#!/usr/bin/env python3
"""Run under dbus-run-session: exercise output loss in an isolated virtual KWin.
The host compositor and its outputs are never changed. Requires python-dbus,
PyGObject, kscreen-doctor, and a built native DaisyOS shell.
"""
import collections
import json
import os
from pathlib import Path
import re
import shlex
import subprocess
import sys
import time

ROOT = Path(__file__).resolve().parents[1]


def session(binary, artifacts):
    env = os.environ.copy()
    env.update(QT_QPA_PLATFORM='wayland', DAISYOS_SHELL_SESSION='1', DAISYOS_WAYLAND_DIAGNOSTICS='1')
    log = artifacts / 'shell.log'
    def outputs():
        return json.loads(subprocess.check_output(['kscreen-doctor', '-j'], env=env, timeout=5))['outputs']
    def configure(name, enabled):
        subprocess.run(['kscreen-doctor', f'output.{name}.{"enable" if enabled else "disable"}'],
                       env=env, check=True, timeout=5, stdout=subprocess.DEVNULL)
    def configured():
        return collections.Counter(re.findall(r"\[daisy-layer\] '([^']+)' acknowledged configure", log.read_text()))
    with log.open('w') as stream:
        shell = subprocess.Popen([binary, '--shell-session'], env=env, stdout=stream, stderr=stream)
        try:
            def wait_for(predicate):
                deadline = time.monotonic() + 12
                while time.monotonic() < deadline:
                    assert shell.poll() is None, f'Shell exited: {log}'
                    text = log.read_text()
                    assert '[daisy-fatal]' not in text and 'invalid object' not in text, f'Wayland failure: {log}'
                    if predicate(): return
                    time.sleep(.05)
                raise AssertionError(f'Surfaces did not recover: {log}')
            wait_for(lambda: len(configured()) >= 16)
            initial = configured()
            names = [o['name'] for o in outputs() if o['enabled']]
            assert len(names) == 2, names
            # The backend chooses the first announced wl_output (Virtual-0).
            first, second = sorted(names)
            configure(first, False)
            wait_for(lambda: all(configured()[name] > count for name, count in initial.items()))
            restored = configured()
            configure(first, True)
            configure(second, False)
            wait_for(lambda: all(configured()[name] > count for name, count in restored.items()))
            configure(second, True)
            assert shell.poll() is None
            (artifacts / 'result.json').write_text(json.dumps(dict(
                passed=True, pid=shell.pid, surfaces=len(initial), outputRemovals=2,
                configured=dict(configured())), indent=2))
            print(f'PASS: {len(initial)} layer surfaces recovered twice without closing or restarting the shell')
        finally:
            shell.kill()
            shell.wait(timeout=5)


def main():
    if sys.argv[1:2] == ['--session']:
        session(sys.argv[2], Path(sys.argv[3]))
        return
    import dbus
    import dbus.service
    import dbus.mainloop.glib
    from gi.repository import GLib
    dbus.mainloop.glib.DBusGMainLoop(set_as_default=True)
    bus = dbus.SessionBus()
    assert not bus.name_has_owner('org.kde.KWin'), 'Run this test under dbus-run-session, away from the host compositor'
    portal = dbus.service.BusName('org.freedesktop.portal.Desktop', bus=bus)
    # Qt asks for appearance before KWin has opened its Wayland socket. Supply
    # isolated appearance settings so portal activation cannot reach the host.
    class Settings(dbus.service.Object):
        @dbus.service.method('org.freedesktop.portal.Settings', in_signature='as', out_signature='a{sa{sv}}')
        def ReadAll(self, names):
            return {}
    settings = Settings(bus, '/org/freedesktop/portal/desktop')
    binary = str(Path(sys.argv[1] if len(sys.argv) > 1 else ROOT / 'src/DaisyOS.Shell/bin/Debug/net10.0/DaisyOS.Shell').resolve())
    artifacts = Path(os.environ.get('DAISYOS_OUTPUT_RECOVERY_ARTIFACTS', ROOT / 'artifacts/compositor/output-recovery')).resolve()
    artifacts.mkdir(parents=True, exist_ok=True)
    runner = artifacts / 'session.sh'
    runner.write_text('#!/bin/sh\nexec ' + shlex.join([sys.executable, str(Path(__file__).resolve()), '--session', binary, str(artifacts)]) + '\n')
    runner.chmod(0o755)
    env = os.environ.copy()
    env.update(QT_QPA_PLATFORM='offscreen', QT_QPA_PLATFORMTHEME='generic', KWIN_COMPOSE='Q',
               LIBGL_ALWAYS_SOFTWARE='1', XDG_CONFIG_HOME=str(artifacts / 'config'),
               XDG_CACHE_HOME=str(artifacts / 'cache'), XDG_DATA_HOME=str(artifacts / 'data'))
    result = artifacts / 'result.json'
    result.unlink(missing_ok=True)
    with (artifacts / 'kwin.log').open('w') as log:
        kwin = subprocess.Popen(['kwin_wayland', '--virtual', '--width', '1280', '--height', '800',
            '--output-count', '2', '--no-lockscreen', '--socket', f'daisy-output-recovery-{os.getpid()}',
            '--exit-with-session', str(runner)], env=env, stdout=log, stderr=log)
        try:
            context = GLib.MainContext.default()
            deadline = time.monotonic() + 45
            while kwin.poll() is None and time.monotonic() < deadline:
                while context.pending(): context.iteration(False)
                time.sleep(.01)
            assert result.exists(), f'Output recovery failed; see {artifacts}'
            print(f'PASS: isolated output recovery; report: {result}')
        finally:
            if kwin.poll() is None: kwin.terminate()
            try: kwin.wait(timeout=3)
            except subprocess.TimeoutExpired: kwin.kill(); kwin.wait()


if __name__ == '__main__':
    main()
