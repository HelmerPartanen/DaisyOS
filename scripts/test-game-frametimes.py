#!/usr/bin/env python3
import importlib.util
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location('frametimes', Path(__file__).with_name('analyze-game-frametimes.py'))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class FrameTimeTests(unittest.TestCase):
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
