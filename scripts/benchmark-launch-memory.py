#!/usr/bin/env python3
"""Compare legacy and Rust launchers on a private bus and disposable XDG catalog.
Includes broker+helper peak PSS and dispatch latency, not just the idle broker.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import statistics
import subprocess
import sys
import tempfile
import threading
import time
import uuid

ROOT = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--legacy', type=Path, required=True)
parser.add_argument('--rust', type=Path, required=True)
parser.add_argument('--baseline-name', default='resident Qt/KDE')
parser.add_argument('--platform', choices=['offscreen', 'wayland'], default='offscreen')
parser.add_argument('--samples', type=int, default=30)
parser.add_argument('--runs', type=int, default=3)
parser.add_argument('--output', type=Path, required=True)
parser.add_argument('--inside', action='store_true', help=argparse.SUPPRESS)
args = parser.parse_args()
if args.samples < 5 or args.runs < 1:
    parser.error('Use at least 5 samples and one run')
args.legacy, args.rust, args.output = args.legacy.resolve(), args.rust.resolve(), args.output.resolve()
if not args.inside:
    with tempfile.TemporaryDirectory(prefix='daisy-launch-memory-') as temp:
        env = os.environ.copy()
        env.update(XDG_DATA_HOME=temp+'/data', XDG_CONFIG_HOME=temp+'/config', XDG_CACHE_HOME=temp+'/cache',
                   XDG_CURRENT_DESKTOP='KDE', QT_QPA_PLATFORM=args.platform, QT_QPA_PLATFORMTHEME='', GTK_USE_PORTAL='0', GIO_USE_PORTALS='0')
        raise SystemExit(subprocess.run(['dbus-run-session', '--', sys.executable, __file__, '--inside',
            '--legacy', str(args.legacy), '--rust', str(args.rust), '--samples', str(args.samples),
            '--runs', str(args.runs), '--baseline-name', args.baseline_name, '--platform', args.platform, '--output', str(args.output)], env=env).returncode)

import dbus
apps = Path(os.environ['XDG_DATA_HOME']) / 'applications'
apps.mkdir(parents=True)
(apps / 'org.daisyos.memoryfixture.desktop').write_text('[Desktop Entry]\nType=Application\nName=Memory fixture\nExec=/bin/true\n')
subprocess.run(['kbuildsycoca6', '--noincremental'], check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)


def memory(pid):
    values = {}
    for name in ['smaps_rollup', 'status']:
        for line in Path(f'/proc/{pid}/{name}').read_text().splitlines():
            key, _, value = line.partition(':')
            if key in ['Rss', 'Pss', 'Private_Clean', 'Private_Dirty', 'VmSize', 'Threads']:
                values[key] = int(value.split()[0])
    return values


def descendants(pid):
    result = [pid]
    try:
        for child in Path(f'/proc/{pid}/task/{pid}/children').read_text().split():
            result.extend(descendants(int(child)))
    except OSError:
        pass
    return result


def run(binary):
    bus = dbus.bus.BusConnection(os.environ['DBUS_SESSION_BUS_ADDRESS'])
    started = time.perf_counter()
    p = subprocess.Popen([str(binary)], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    stop = threading.Event()
    peaks = []
    def sampler():
        while not stop.wait(.01):
            rows = []
            for pid in descendants(p.pid):
                try:
                    rows.append(memory(pid))
                except OSError:
                    pass
            if rows:
                peaks.append(sum(r['Pss'] for r in rows))
    sampling = threading.Thread(target=sampler)
    try:
        end = time.monotonic() + 20
        while not bus.name_has_owner('org.daisyos.Launcher1'):
            if p.poll() is not None or time.monotonic() > end:
                raise RuntimeError('Launcher did not become ready')
            time.sleep(.005)
        startup = (time.perf_counter() - started) * 1000
        api = dbus.Interface(bus.get_object('org.daisyos.Launcher1', '/org/daisyos/Launcher1'), 'org.daisyos.Launcher1')
        cold = memory(p.pid)
        catalog = json.loads(api.Catalog())
        assert any(v['id'] == 'org.daisyos.memoryfixture.desktop' for v in catalog)
        sampling.start()
        samples = []
        traces = []
        first_dispatch = None
        for index in range(args.samples + 3):
            request = uuid.uuid4().hex
            begin = time.perf_counter()
            assert api.Launch(request, 'org.daisyos.memoryfixture.desktop', '', dbus.Array([], signature='s'), '', dbus.Dictionary({}, signature='sv'))
            assert api.Await(request)
            elapsed = (time.perf_counter() - begin) * 1000
            if index == 0: first_dispatch = elapsed
            if index >= 3:
                samples.append(elapsed)
                traces.append(json.loads(api.Trace(request)))
            # Keep the same low-rate workload within the API's 20/sec limit.
            time.sleep(.06)
        burst_tree = []
        for pid in descendants(p.pid):
            try: burst_tree.append(memory(pid))
            except OSError: pass
        deadline = time.monotonic() + 3
        while descendants(p.pid)[1:] and time.monotonic() < deadline: time.sleep(.02)
        warm = memory(p.pid)
        helpers = descendants(p.pid)[1:]
        assert not helpers, f'Helper processes remained resident after idle lease: {helpers}'
        return {'startupMs': startup, 'firstDispatchMs': first_dispatch, 'burstTreePssKiB': sum(r['Pss'] for r in burst_tree), 'coldIdle': cold, 'warmIdle': warm,
                'peakTreePssKiB': max(peaks, default=0), 'dispatchMs': samples, 'traces': traces,
                'catalogEntries': len(catalog), 'helpersAfterWorkload': len(helpers)}
    finally:
        stop.set()
        if sampling.is_alive():
            sampling.join(2)
        p.terminate()
        p.wait(timeout=5)
        bus.close()


results = {'legacy': [], 'rust': []}
for index in range(args.runs):
    for label in (['legacy', 'rust'] if index % 2 == 0 else ['rust', 'legacy']):
        row = run(args.legacy if label == 'legacy' else args.rust)
        results[label].append(row)
        print(label, 'warm PSS', row['warmIdle']['Pss'], 'KiB, median dispatch', round(statistics.median(row['dispatchMs']), 2), 'ms', flush=True)
summary = {}
for label, rows in results.items():
    times = sorted(t for r in rows for t in r['dispatchMs'])
    stages = {}
    for row in rows:
        for trace in row['traces']:
            for stage, begin, end in [('helper.prepare','backend_started','dispatch_started'), ('environment','dispatch_started','environment_ready'), ('kio','environment_ready','dispatch_accepted')]:
                if trace.get(begin) and trace.get(end): stages.setdefault(stage, []).append((int(trace[end])-int(trace[begin]))/1e6)
    summary[label] = {'stageMedianMs': {key: statistics.median(values) for key, values in stages.items()},'warmPssKiB': statistics.median(r['warmIdle']['Pss'] for r in rows),
        'warmRssKiB': statistics.median(r['warmIdle']['Rss'] for r in rows),
        'peakTreePssKiB': max(r['peakTreePssKiB'] for r in rows),
        'dispatchMedianMs': statistics.median(times), 'dispatchP95Ms': times[min(len(times)-1, __import__('math').ceil(.95*len(times))-1)],
        'firstDispatchMedianMs': statistics.median(r['firstDispatchMs'] for r in rows),
        'burstTreePssKiB': statistics.median(r['burstTreePssKiB'] for r in rows),
        'startupMedianMs': statistics.median(r['startupMs'] for r in rows)}
binaries = {name: hashlib.sha256(path.read_bytes()).hexdigest() for name, path in
            [('legacy', args.legacy), ('rust', args.rust), ('backend', args.rust.with_name('daisy-launch-backend'))]}
if args.legacy.with_name('daisy-launch-backend').exists():
    binaries['baselineBackend'] = hashlib.sha256(args.legacy.with_name('daisy-launch-backend').read_bytes()).hexdigest()
report = {'comparisonBaseline': args.baseline_name, 'binarySha256': binaries, 'rustc': subprocess.check_output(['rustc', '--version'], text=True).strip(),
          'qt': subprocess.check_output(['pkg-config', '--modversion', 'Qt6Core'], text=True).strip(),
          'units': 'KiB, ms, thread counts', 'runs': args.runs, 'samplesPerRun': args.samples,
          'workload': f'private session bus, {args.platform} Qt, real KIO /bin/true dispatch, 3 warmups, low request rate, warm idle measured after helper lease expiry; readiness is dispatch acceptance, not application readiness',
          'summary': summary, 'raw': results}
args.output.parent.mkdir(parents=True, exist_ok=True)
args.output.write_text(json.dumps(report, indent=2) + '\n')
print(json.dumps(summary, indent=2))
