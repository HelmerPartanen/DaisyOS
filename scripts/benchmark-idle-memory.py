#!/usr/bin/env python3
"""A/B idle memory on a real Wayland compositor. Does not alter the session.
Build C with -DDAISY_IDLE_C_BASELINE=ON in a separate directory first.
PSS is the attributable physical footprint; VmSize includes unused reservations.
"""
import argparse
import json
from pathlib import Path
import statistics
import subprocess
import tempfile
import time

ROOT = Path(__file__).resolve().parents[1]
METRICS = ['Rss', 'Pss', 'Private_Clean', 'Private_Dirty', 'VmSize', 'Threads']
HARNESS = r'''
#include <dlfcn.h>
#include <stdint.h>
#include <unistd.h>
int main(int argc, char **argv) {
    if (argc != 2) return 2;
    void *lib = dlopen(argv[1], RTLD_NOW);
    if (!lib) return 3;
    void *(*start)(uint32_t, void *, void *, void *) = dlsym(lib, "daisy_idle_monitor_start");
    void (*stop)(void *) = dlsym(lib, "daisy_idle_monitor_stop");
    int (*available)(void) = dlsym(lib, "daisy_idle_monitor_is_available");
    if (!start || !stop || !available || !available()) return 4;
    void *handle = start(600000, 0, 0, 0);
    if (!handle) return 5;
    char byte;
    while (read(0, &byte, 1) > 0) {}
    stop(handle);
    dlclose(lib);
    return 0;
}
'''


def memory(pid):
    values = {}
    for name in ['smaps_rollup', 'status']:
        for line in Path(f'/proc/{pid}/{name}').read_text().splitlines():
            key, _, value = line.partition(':')
            if key in METRICS:
                values[key] = int(value.split()[0])
    return values


def sample(command, delay):
    p = subprocess.Popen(command, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    try:
        time.sleep(delay)
        if p.poll() is not None:
            raise RuntimeError(f'{command[0]} exited early: {p.stderr.read().decode()}')
        result = memory(p.pid)
        p.stdin.close()
        assert p.wait(timeout=5) == 0, p.stderr.read().decode()
        return result
    finally:
        if p.poll() is None:
            p.kill()
            p.wait()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--baseline-dir', type=Path, required=True)
    parser.add_argument('--rust-dir', type=Path, default=ROOT / 'src/native/daisy_idle_monitor/build')
    parser.add_argument('--samples', type=int, default=9)
    parser.add_argument('--settle', type=float, default=.3)
    parser.add_argument('--output', type=Path)
    args = parser.parse_args()
    if args.samples < 3 or args.settle <= 0:
        parser.error('use at least 3 samples and a positive settle interval')
    report = {'units': 'KiB except Threads (count)', 'samples': args.samples, 'results': {}}
    with tempfile.TemporaryDirectory(prefix='daisy-idle-bench-') as temp:
        source = Path(temp) / 'host.c'
        source.write_text(HARNESS)
        host = Path(temp) / 'host'
        subprocess.run(['cc', '-Os', '-Wall', '-Wextra', '-Werror', str(source), '-ldl', '-o', str(host)], check=True)
        for mode in ['standalone', 'library']:
            samples = {'c': [], 'rust': []}
            for index in range(args.samples):
                # Alternate order to reduce page-cache/host-load bias.
                for label in (['c', 'rust'] if index % 2 == 0 else ['rust', 'c']):
                    directory = args.baseline_dir if label == 'c' else args.rust_dir
                    command = [str(directory / 'daisyos-idle-monitor')] if mode == 'standalone' else [str(host), str(directory.resolve() / 'libdaisy_idle.so')]
                    samples[label].append(sample(command, args.settle))
            median = {label: {key: statistics.median(s[key] for s in rows) for key in METRICS} for label, rows in samples.items()}
            report['results'][mode] = {'median': median, 'saved': {k: median['c'][k] - median['rust'][k] for k in METRICS}, 'raw': samples}
    text = json.dumps(report, indent=2) + '\n'
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(text)
    print(text)


if __name__ == '__main__':
    main()
