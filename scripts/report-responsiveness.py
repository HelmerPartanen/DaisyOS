#!/usr/bin/env python3
"""Summarize opt-in UI timings. A render callback is not compositor presentation."""
import argparse
import collections
import json
import math


def summarize(entries, refresh_hz=None):
    groups = collections.defaultdict(list)
    for entry in entries:
        value = entry.get('durationMs')
        if value is not None and math.isfinite(value) and value >= 0 and not entry['stage'].startswith('runtime.'):
            groups[entry['stage']].append(value)

    def percentile(values, fraction):
        return values[max(0, math.ceil(len(values) * fraction) - 1)]

    timings = {stage: {'samples': len(values), 'p50Ms': percentile(sorted(values), .5),
                      'p95Ms': percentile(sorted(values), .95), 'worstMs': max(values)}
               for stage, values in groups.items()}
    if refresh_hz is None:
        return timings
    interval = 1000 / refresh_hz
    callbacks = {stage: {'samples': len(values),
                        'overOneAndAHalfRefreshIntervals': sum(value > 1.5 * interval for value in values),
                        'fractionWithinBudget': sum(value <= 1.5 * interval for value in values) / len(values)}
                 for stage, values in groups.items() if stage.endswith('.frame.interval')}
    return {'presentationMeasured': False, 'refreshHz': refresh_hz, 'timings': timings,
            'callbackCadence': callbacks,
            'interpretation': 'UI callback intervals only. Actual presentation and physical input latency require separate capture.'}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('trace')
    parser.add_argument('--refresh-hz', type=float)
    args = parser.parse_args()
    if args.refresh_hz is not None and (not math.isfinite(args.refresh_hz) or args.refresh_hz <= 0):
        parser.error('Refresh rate must be finite and positive.')
    with open(args.trace) as source:
        print(json.dumps(summarize((json.loads(line) for line in source), args.refresh_hz), indent=2))


if __name__ == '__main__':
    main()
