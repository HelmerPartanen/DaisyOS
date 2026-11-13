#!/usr/bin/env python3
"""Alternate isolated app launches with two shell assemblies; capture PSS at readiness.

This diagnostic is separate from the fixed OS score. It uses the same authenticated
render-ready signal and fixture data, and never restarts the desktop or user apps.
"""
import argparse
import importlib.util
import json
import os
from pathlib import Path
import shutil
import statistics
import subprocess
import tempfile
import time
import uuid

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('os_benchmark', Path(__file__).with_name('benchmark-os-performance.py'))
benchmark = importlib.util.module_from_spec(spec)
spec.loader.exec_module(benchmark)


def resources(pid):
    row = {}
    for name in ('smaps_rollup', 'status'):
        for line in Path(f'/proc/{pid}/{name}').read_text().splitlines():
            key, _, value = line.partition(':')
            if key in ('Pss', 'Rss', 'Private_Clean', 'Private_Dirty', 'Threads'):
                row[key] = int(value.split()[0])
    if 'Pss' not in row:
        raise RuntimeError('PSS coverage unavailable; resource comparison is incomplete')
    return row


def variant(directory, source, shell, application):
    directory.mkdir()
    # All dependencies are identical; only the nominated Shell assembly varies.
    # Copy the entry DLL so the host resolves dependencies beside this variant.
    for path in source.iterdir():
        target = directory / path.name
        if path.name == 'DaisyOS.Shell.dll':
            shutil.copy2(shell, target)
        elif path.name.startswith('DaisyOS.' + application + '.'):
            shutil.copy2(path, target)
        else:
            target.symlink_to(path, target_is_directory=path.is_dir())
    return directory / ('DaisyOS.' + application + '.dll')


def launch(entry, app, scratch, log, timeout):
    nonce = uuid.uuid4().hex
    signal_path = scratch / (nonce + '.json')
    env = os.environ.copy()
    env.pop('DAISYOS_STARTUP_TRACE', None)
    env.update(DAISYOS_PERFORMANCE_RESULT=str(signal_path), DAISYOS_PERFORMANCE_TOKEN=nonce,
               DAISYOS_PERFORMANCE_DATA=str(scratch), XDG_CONFIG_HOME=str(scratch / 'config'),
               XDG_STATE_HOME=str(scratch / 'state'), XDG_CACHE_HOME=str(scratch / 'cache'))
    command = ['dotnet', str(entry)] + ([str(scratch / 'files')] if app == 'files' else [])
    process = None
    started = time.perf_counter()
    try:
        with log.open('w') as output:
            process = subprocess.Popen(command, env=env, stdout=output, stderr=subprocess.STDOUT, start_new_session=True)
            while not signal_path.exists():
                if process.poll() is not None:
                    raise RuntimeError(f'App exited before readiness; see {log}')
                if time.perf_counter() - started > timeout:
                    raise TimeoutError(f'App timed out; see {log}')
                time.sleep(.002)
            elapsed = (time.perf_counter() - started) * 1000
            benchmark.validate_signal(json.loads(signal_path.read_text()), process.pid, nonce, app)
            row = resources(process.pid)
            row['elapsedMs'] = elapsed
            process.wait(timeout=5)
            if process.returncode:
                raise RuntimeError(f'App shutdown failed; see {log}')
            return row
    finally:
        if process is not None and process.poll() is None:
            process.terminate()
            try:
                process.wait(timeout=3)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait()


def summarize(rows):
    result = {}
    for key in rows[0]:
        values = [row[key] for row in rows]
        median = statistics.median(values)
        result[key] = dict(median=median, mad=statistics.median(abs(v - median) for v in values),
                           minimum=min(values), maximum=max(values))
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--app', choices=('notes', 'settings', 'files'), default='notes')
    parser.add_argument('--baseline-shell', type=Path, required=True)
    parser.add_argument('--candidate-shell', type=Path, required=True)
    parser.add_argument('--samples', type=int, default=7)
    parser.add_argument('--warmups', type=int, default=2)
    parser.add_argument('--timeout', type=float, default=30)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    if args.samples < 5 or args.warmups < 1 or not 0 < args.timeout < float('inf'):
        parser.error('Use at least five samples, one warmup and a finite positive timeout')
    if args.output.exists():
        parser.error('Output already exists')
    app_name = args.app.title()
    source = ROOT / 'src' / ('DaisyOS.' + app_name) / 'bin' / 'Release' / 'net10.0'
    if not (source / ('DaisyOS.' + app_name + '.dll')).is_file():
        parser.error('Build the Release app first')
    shells = {label: path.resolve(strict=True) for label, path in
              [('baseline', args.baseline_shell), ('candidate', args.candidate_shell)]}
    logs = args.output.resolve().with_suffix('.logs')
    logs.mkdir(parents=True, exist_ok=False)
    report = dict(protocol=1, app=args.app, samples=args.samples, warmups=args.warmups,
                  environment=benchmark.environment('Release'), status='running',
                  shellHashes={label: benchmark.file_hash(path) for label, path in shells.items()},
                  sharedRuntimeHashes={p.name: benchmark.file_hash(p) for p in sorted(source.glob('*.dll')) if p.name != 'DaisyOS.Shell.dll'},
                  boundary='dotnet launch to authenticated render-ready signal; PSS/RSS KiB immediately afterward, not peak or settled memory',
                  presentationMeasured=False, rows={'baseline': [], 'candidate': []})
    args.output.parent.mkdir(parents=True, exist_ok=True)
    try:
        with tempfile.TemporaryDirectory(prefix='daisy-startup-resources-') as temporary:
            scratch = Path(temporary)
            report['fixtureHash'] = benchmark.fixtures(scratch)
            config = scratch / 'config' / 'DaisyOS'
            config.mkdir(parents=True)
            appearance = Path(os.environ.get('XDG_CONFIG_HOME', str(Path.home() / '.config'))) / 'DaisyOS' / 'appearance.json'
            report['environment']['comparable']['appearance'] = benchmark.snapshot_appearance(appearance, config / 'appearance.json')
            entries = {label: variant(scratch / label, source, shell, app_name) for label, shell in shells.items()}
            for run in range(-args.warmups, args.samples):
                for label in (('baseline', 'candidate') if run % 2 == 0 else ('candidate', 'baseline')):
                    row = launch(entries[label], args.app, scratch, logs / f'{label}-{run}.log', args.timeout)
                    print(label, run, row, flush=True)
                    if run >= 0:
                        report['rows'][label].append(row)
        report['summary'] = {label: summarize(rows) for label, rows in report['rows'].items()}
        report['status'] = 'complete'
    except Exception as error:
        report['status'] = 'failed'
        report['error'] = str(error)
        raise
    finally:
        args.output.write_text(json.dumps(report, indent=2) + '\n')


if __name__ == '__main__':
    main()
