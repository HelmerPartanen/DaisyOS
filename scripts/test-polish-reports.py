#!/usr/bin/env python3
import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location('report', Path(__file__).with_name('report-responsiveness.py'))
report = importlib.util.module_from_spec(spec)
spec.loader.exec_module(report)


class PolishReportTests(unittest.TestCase):
    def test_callback_budget_does_not_claim_presentation(self):
        entries = [{'stage': 'motion.frame.interval', 'durationMs': ms} for ms in [8, 8, 25]]
        result = report.summarize(entries, 120)
        self.assertFalse(result['presentationMeasured'])
        cadence = result['callbackCadence']['motion.frame.interval']
        self.assertEqual(1, cadence['overOneAndAHalfRefreshIntervals'])
        self.assertAlmostEqual(2 / 3, cadence['fractionWithinBudget'])

    def test_runtime_counters_and_invalid_samples_are_not_latency(self):
        result = report.summarize([
            {'stage': 'runtime.gc.heap-bytes', 'durationMs': 1000000},
            {'stage': 'motion.frame.interval', 'durationMs': float('nan')},
            {'stage': 'motion.frame.interval', 'durationMs': -1},
            {'stage': 'launcher.search.projection', 'durationMs': 3}])
        self.assertEqual(['launcher.search.projection'], list(result))

    def test_empty_capture_does_not_invent_successful_frames(self):
        self.assertEqual({}, report.summarize([], 180)['callbackCadence'])


if __name__ == '__main__':
    unittest.main()
