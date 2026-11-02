#!/usr/bin/env python3
import importlib.util
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location('frametimes', Path(__file__).with_name('analyze-game-frametimes.py'))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
compare_spec = importlib.util.spec_from_file_location('comparison', Path(__file__).with_name('compare-game-frametimes.py'))
comparison = importlib.util.module_from_spec(compare_spec)
compare_spec.loader.exec_module(comparison)


class FrameTimeTests(unittest.TestCase):
    def test_comparison_requires_repeated_runs_and_does_not_certify_gaming(self):
        baseline = [module.summarize([10] * 1000)] * 5
        with self.assertRaises(ValueError):
            comparison.compare(baseline[:4], baseline)
        result = comparison.compare(baseline, baseline)
        self.assertEqual('within_baseline_variation', result['status'])
        self.assertFalse(result['gamingGatePassed'])

    def test_comparison_catches_slow_tail_even_with_a_fast_average(self):
        baseline = [module.summarize([10] * 1000)] * 5
        candidate = [module.summarize([9] * 990 + [50] * 10)] * 5
        result = comparison.compare(baseline, candidate)
        self.assertEqual('regression', result['status'])
        self.assertEqual('regression', result['metrics']['one_percent_low_fps']['status'])

    def test_noisy_baseline_cannot_establish_a_pass(self):
        baseline = [module.summarize([time] * 1000) for time in [8, 9, 10, 11, 12]]
        result = comparison.compare(baseline, [module.summarize([10] * 1000)] * 5)
        self.assertEqual('inconclusive', result['status'])

    def test_slow_tail_is_distinct_from_average(self):
        result = module.summarize([10] * 990 + [50] * 10)
        self.assertEqual(20, result['one_percent_low_fps'])
        self.assertAlmostEqual(1000000 / 10400, result['average_fps'])
        self.assertEqual(10, result['p99_frametime_ms'])
        self.assertEqual(50, result['max_frametime_ms'])
        self.assertEqual(10, result['over_33_33_ms'])

    def test_invalid_samples_do_not_poison_summary(self):
        self.assertEqual(100, module.summarize([10, float('nan'), float('inf'), -1, 0])['average_fps'])
        with self.assertRaises(ValueError):
            module.summarize([])

    def test_metadata_partial_rows_and_warmup(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'game.csv'
            path.write_text('os,cpu\nLinux,CPU\nfps,frametime\n10,100\n10,100\npartial\n100,10\n')
            self.assertEqual([10], list(module.read_samples(path, skip_seconds=0.2)))


if __name__ == '__main__':
    unittest.main()
