#!/usr/bin/env python3
"""Compare real GUI readiness through direct, desktop/GIO and persistent KIO routes.

All routes use the same Release binary and isolated data. Does not activate or
close user windows. Requires dbus-python and an active DaisyLaunch service.
"""
import argparse
import importlib.util
import json
import os
from pathlib import Path
import random
import signal
import statistics
import subprocess
import tempfile
import time
import uuid
import dbus

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('os_performance', ROOT / 'scripts/benchmark-os-performance.py')
bench = importlib.util.module_from_spec(spec)
spec.loader.exec_module(bench)
parser = argparse.ArgumentParser()
parser.add_argument('--app', choices=['settings', 'notes', 'files'], default='settings')
parser.add_argument('--samples', type=int, default=3)
parser.add_argument('--output', type=Path, required=True)
args = parser.parse_args()
if args.samples < 1 or args.output.exists(): raise SystemExit('Positive samples and a new output path are required')
binary = ROOT / 'src' / ('DaisyOS.' + args.app.title()) / 'bin/Release/net10.0' / ('DaisyOS.' + args.app.title())
if not binary.is_file(): raise SystemExit('Build the Release app first')
bus = dbus.SessionBus(private=True)
api = dbus.Interface(bus.get_object('org.daisyos.Launcher1', '/org/daisyos/Launcher1'), 'org.daisyos.Launcher1')
app_dir = Path(os.environ.get('XDG_DATA_HOME', Path.home() / '.local/share')) / 'applications'
app_dir.mkdir(parents=True, exist_ok=True)
entry_id = 'org.daisyos.launch-route-test.' + uuid.uuid4().hex + '.desktop'
entry = app_dir / entry_id
rows = []
with tempfile.TemporaryDirectory(prefix='daisy-gui-route-') as temp:
    scratch = Path(temp)
    fixture_hash = bench.fixtures(scratch)
    routes = ['direct', 'desktop-gio', 'persistent-kio'] * args.samples
    random.Random(1337).shuffle(routes)
    nonce = None
    try:
        for route in routes:
            nonce = uuid.uuid4().hex
            result = scratch / (nonce + '.json')
            env = {'DAISYOS_PERFORMANCE_RESULT': str(result), 'DAISYOS_PERFORMANCE_TOKEN': nonce,
                   'DAISYOS_PERFORMANCE_DATA': str(scratch), 'XDG_CONFIG_HOME': str(scratch / 'config'),
                   'XDG_STATE_HOME': str(scratch / 'state'), 'XDG_CACHE_HOME': str(scratch / 'cache')}
            command = ['/usr/bin/env', *[key + '=' + value for key, value in env.items()], str(binary)]
            if args.app == 'files': command.append(str(scratch / 'files'))
            # Standard Desktop Entry double-quoting, not shell execution.
            def quote(value):
                return '"' + value.replace('\\', '\\\\\\\\').replace('"', '\\\\"').replace('$', '\\\\$').replace('`', '\\\\`') + '"'
            entry.write_text('[Desktop Entry]\nType=Application\nName=DaisyOS launch route fixture\nExec=' + ' '.join(map(quote, command)) + '\n')
            if route == 'persistent-kio':
                deadline = time.monotonic() + 15
                while True:
                    catalog = json.loads(str(api.Catalog()))
                    if any(app['id'] == entry_id and any(nonce in arg for arg in app['argv']) for app in catalog): break
                    if time.monotonic() > deadline: raise TimeoutError('Fixture registry did not refresh')
                    time.sleep(.1)
            process = None
            started = time.perf_counter_ns()
            if route == 'direct': process = subprocess.Popen(command, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            elif route == 'desktop-gio': process = subprocess.Popen(['gio', 'launch', str(entry)], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            else:
                request = uuid.uuid4().hex
                if not api.Launch(request, entry_id, '', dbus.Array([], signature='s'), '', dbus.Dictionary({}, signature='sv')):
                    raise RuntimeError('Launch was rejected')
                api.Await(request)
            while not result.exists():
                if time.perf_counter_ns() - started > 30_000_000_000: raise TimeoutError(route + ' GUI readiness timeout')
                time.sleep(.002)
            elapsed = (time.perf_counter_ns() - started) / 1e6
            signal = json.loads(result.read_text())
            bench.validate_signal(signal, signal['processId'], nonce, args.app)
            rows.append({'route': route, 'readyMs': elapsed})
            print(f'{route}: {elapsed:.2f} ms', flush=True)
            if process is not None: process.wait(timeout=5)
            time.sleep(.3) # Fixture window closes itself; outside the measured interval.
    finally:
        # Close only a still-running fixture bearing our exact nonce and executable.
        # This also handles failure before the readiness handshake is published.
        if nonce is not None:
            marker = ('DAISYOS_PERFORMANCE_TOKEN=' + nonce).encode()
            for candidate in Path('/proc').iterdir():
                if not candidate.name.isdigit(): continue
                try:
                    if marker in (candidate / 'environ').read_bytes().split(b'\0') and (candidate / 'exe').resolve() == binary.resolve():
                        os.kill(int(candidate.name), signal.SIGTERM)
                except (FileNotFoundError, PermissionError, ProcessLookupError): pass
        entry.unlink(missing_ok=True)
        bus.close()
report = {'date': time.strftime('%Y-%m-%d %H:%M:%S'), 'app': args.app,
          'configuration': 'Release', 'binary': str(binary), 'binarySha256': bench.file_hash(binary),
          'fixtureSha256': fixture_hash, 'samples': args.samples, 'raw': rows,
          'medianMs': {route: statistics.median(row['readyMs'] for row in rows if row['route'] == route)
                       for route in ('direct', 'desktop-gio', 'persistent-kio')},
          'note': 'Process/dispatch to initial content rendered, not physical presentation or pointer-click latency; identical isolated app data'}
args.output.parent.mkdir(parents=True, exist_ok=True)
args.output.write_text(json.dumps(report, indent=2) + '\n')
print(json.dumps(report['medianMs'], indent=2))
