#!/usr/bin/env python3
"""Summarize a MangoHud CSV without confusing average FPS with tail latency."""
import argparse
import csv
import json
import math
from pathlib import Path


def summarize(frametimes):
    values = sorted(value for value in frametimes if math.isfinite(value) and value > 0)
    if not values:
        raise ValueError('No valid frame times in the selected interval.')
    count = len(values)
    slowest_count = max(1, math.ceil(count * 0.01))
    slowest = values[-slowest_count:]
    return {
        'samples': count,
        'average_fps': 1000 * count / sum(values),
        'one_percent_low_fps': 1000 * len(slowest) / sum(slowest),
        'p99_frametime_ms': values[max(0, math.ceil(count * 0.99) - 1)],
        'max_frametime_ms': values[-1],
        'over_16_67_ms': sum(value > 1000 / 60 for value in values),
        'over_33_33_ms': sum(value > 1000 / 30 for value in values),
    }


def read_samples(path, skip_seconds=0, duration=None):
    with Path(path).open(newline='') as stream:
        rows = csv.reader(stream)
        headers = None
        for _ in range(12):
            row = next(rows, None)
            if row is None:
                break
            row = [value.strip() for value in row]
            if 'frametime' in row:
                headers = row
                break
        if headers is None:
            raise ValueError('A MangoHud frame log with a frametime column is required.')
        index = headers.index('frametime')
        elapsed = 0.0
        for row in rows:
            if len(row) != len(headers):
                continue
            try:
                value = float(row[index])
            except ValueError:
                continue
            if not math.isfinite(value) or value <= 0:
                continue
            # log_interval=0 records each frame; elapsed time is accumulated
            # from frame times so no MangoHud elapsed-column version is assumed.
            elapsed += value / 1000
            if elapsed <= skip_seconds:
                continue
            if duration is not None and elapsed > skip_seconds + duration:
                break
            yield value


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('csv', type=Path)
    parser.add_argument('--skip-seconds', type=float, default=0)
    parser.add_argument('--duration', type=float)
    args = parser.parse_args()
    if args.skip_seconds < 0 or (args.duration is not None and args.duration <= 0):
        parser.error('Intervals must be positive (skip may be zero).')
    try:
        print(json.dumps(summarize(read_samples(args.csv, args.skip_seconds, args.duration)), indent=2))
    except (OSError, ValueError) as error:
        parser.exit(1, f'{error}\n')


if __name__ == '__main__':
    main()
