#!/usr/bin/env python3
import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location("report", Path(__file__).with_name("report-responsiveness.py"))
report = importlib.util.module_from_spec(spec)
spec.loader.exec_module(report)


class ReportTests(unittest.TestCase):
    def test_callbacks_and_counters_have_separate_units(self):
        result = report.summarize_trace([
            {"stage": "motion.frame.interval", "operation": "shell", "durationMs": value}
            for value in (8, 9, 20)
        ] + [{"stage": "runtime.gc.heap-bytes", "durationMs": 123456},
             {"stage": "input.received", "operation": "calendar", "durationMs": None}], 120)
        self.assertFalse(result["presentationMeasured"])
        self.assertAlmostEqual(result["frameBudgetMs"], 1000 / 120)
        metric = result["metrics"]["motion.frame.interval:shell"]
        self.assertEqual(metric["medianMs"], 9)
        self.assertEqual(metric["p95Ms"], 20)
        self.assertEqual(metric["callbackIntervalsOver1_5Budgets"], 1)
        self.assertEqual(result["lastCounters"]["runtime.gc.heap-bytes:"], 123456)
        self.assertEqual(len(result["metrics"]), 1)

    def test_missing_rate_does_not_invent_frame_budget(self):
        result = report.summarize_trace([])
        self.assertIsNone(result["frameBudgetMs"])
        self.assertEqual(result["metrics"], {})

    def test_invalid_samples_and_rates_fail(self):
        for rate in (0, -1, float("nan")):
            with self.assertRaises(ValueError):
                report.summarize_trace([], rate)
        with self.assertRaises(ValueError):
            report.summarize_trace([{"stage": "feedback.callback", "durationMs": float("inf")}])


if __name__ == "__main__":
    unittest.main()
