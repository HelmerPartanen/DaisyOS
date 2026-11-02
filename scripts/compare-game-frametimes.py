#!/usr/bin/env python3
"""Compare repeated per-frame game logs; missing/noisy evidence never passes a gate."""
import argparse
import importlib.util
import json
from pathlib import Path
import statistics

spec = importlib.util.spec_from_file_location('frametimes', Path(__file__).with_name('analyze-game-frametimes.py'))
frametimes = importlib.util.module_from_spec(spec)
spec.loader.exec_module(frametimes)


def compare(baseline, candidate):
    if len(baseline) < 5 or len(candidate) < 5:
        raise ValueError('At least five independent runs per build are required.')
    metrics = {}
    for key, higher_is_better in [('average_fps', True), ('one_percent_low_fps', True),
                                  ('p99_frametime_ms', False)]:
        before = [run[key] for run in baseline]
        after = [run[key] for run in candidate]
        center = statistics.median(before)
        # Baseline repeatability defines the observable envelope. This is a
        # descriptive comparison, not a statistical equivalence certification.
        spread = max(before) - min(before)
        delta = statistics.median(after) - center
        harm = -delta if higher_is_better else delta
        status = 'regression' if harm > spread else 'within_baseline_variation'
        if spread > center * 0.05:
            status = 'inconclusive'
        metrics[key] = {'baselineMedian': center, 'candidateMedian': statistics.median(after),
                        'baselineRange': spread, 'status': status}
    # Compare rates rather than raw counts when capture lengths differ.
    for threshold in ['over_16_67_ms', 'over_33_33_ms']:
        before = [run[threshold] / run['samples'] for run in baseline]
        after = [run[threshold] / run['samples'] for run in candidate]
        spread = max(before) - min(before)
        harm = statistics.median(after) - statistics.median(before)
        metrics[threshold + '_fraction'] = {
            'baselineMedian': statistics.median(before), 'candidateMedian': statistics.median(after),
            'baselineRange': spread,
            'status': 'regression' if harm > spread else 'within_baseline_variation'}
    statuses = {metric['status'] for metric in metrics.values()}
    status = 'regression' if 'regression' in statuses else 'inconclusive' if 'inconclusive' in statuses else 'within_baseline_variation'
    return {'status': status, 'metrics': metrics, 'gamingGatePassed': False,
            'remainingChecks': ['matching environment/workload and alternating run order',
                                'direct scanout and VRR', 'shell CPU/GPU contention',
                                'fullscreen, borderless, windowed and overlay cases'],
            'interpretation': 'Descriptive repeated-run comparison; not proof of zero regression.'}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--baseline', nargs='+', required=True, type=Path)
    parser.add_argument('--candidate', nargs='+', required=True, type=Path)
    parser.add_argument('--skip-seconds', type=float, default=0)
    parser.add_argument('--duration', type=float)
    args = parser.parse_args()
    if args.skip_seconds < 0 or (args.duration is not None and args.duration <= 0):
        parser.error('Intervals must be positive (skip may be zero).')
    paths = [path.resolve() for path in args.baseline + args.candidate]
    if len(set(paths)) != len(paths):
        parser.error('Each run must be a distinct capture file.')
    try:
        def load(paths):
            return [frametimes.summarize(frametimes.read_samples(path, args.skip_seconds, args.duration)) for path in paths]
        result = compare(load(args.baseline), load(args.candidate))
    except (OSError, ValueError) as error:
        parser.exit(2, str(error) + '\n')
    print(json.dumps(result, indent=2))
    if result['status'] != 'within_baseline_variation':
        parser.exit(1)


if __name__ == '__main__':
    main()
