#!/usr/bin/env python3
"""Scoring, compatibility and failure checks for the performance protocol."""
import contextlib
import copy
import importlib.util
import io
import math
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location("performance", Path(__file__).with_name("benchmark-os-performance.py"))
performance = importlib.util.module_from_spec(spec)
spec.loader.exec_module(performance)


class PerformanceProtocolTests(unittest.TestCase):
    def test_appearance_snapshot_is_exact_and_explicit_missing_or_invalid_input_fails(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source, target = root / 'source.json', root / 'isolated.json'
            self.assertEqual({}, performance.snapshot_appearance(source, target))
            self.assertFalse(target.exists())
            with self.assertRaisesRegex(ValueError, 'does not exist'):
                performance.snapshot_appearance(source, target, required=True)
            source.write_text('{"taskbarSize": 2}\n')
            self.assertEqual({'taskbarSize': 2}, performance.snapshot_appearance(source, target, required=True))
            self.assertEqual(source.read_bytes(), target.read_bytes())
            source.write_text('[]')
            with self.assertRaisesRegex(ValueError, 'JSON object'):
                performance.snapshot_appearance(source, target, required=True)

    def metrics(self, multiplier=1):
        return {name: performance.summarize([reference * multiplier] * 7)
                for name, (reference, _, _) in performance.METRICS.items()}

    def report(self, multiplier=1):
        return {"status": "passed", "protocol": performance.PROTOCOL, "fixture": performance.FIXTURE,
                "fixtureHash": "fixture-v1", "samples": 7, "warmups": 2,
                "cachePolicy": "warm", "environment": {"comparable": {"cpu": "test", "configuration": "Release"}},
                "metrics": {name: value | {"referenceMs": performance.METRICS[name][0], "weight": performance.METRICS[name][1]} for name, value in self.metrics(multiplier).items()}}

    def test_chrome_requires_paint_signal_and_cleans_up_on_timeout(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            binary = root / "fake-chrome"
            binary.write_text("#!/bin/sh\nexec sleep 60\n")
            binary.chmod(0o755)
            with self.assertRaisesRegex(TimeoutError, "Chrome startup timed out"):
                performance.chrome_startup(binary, root, root / "chrome.log", .05)
            self.assertFalse(list(root.glob("chrome-*")))

    def test_chrome_comparison_is_separate_and_version_guarded(self):
        before, after = self.report(), self.report()
        for report, latency in ((before, 1000), (after, 500)):
            report["thirdPartyMetrics"] = {"startup.chrome": performance.summarize([latency] * 7) |
                {"protocol": "chrome-local-fcp-v1", "version": "Chrome test"}}
        with contextlib.redirect_stdout(io.StringIO()) as console:
            self.assertAlmostEqual(1, performance.compare_reports(before, after))
        self.assertIn("startup.chrome", console.getvalue())
        self.assertIn("-50.0%", console.getvalue())
        after["thirdPartyMetrics"]["startup.chrome"]["version"] = "Chrome newer"
        with contextlib.redirect_stdout(io.StringIO()) as console:
            performance.compare_reports(before, after)
        self.assertIn("version or workload changed", console.getvalue())

    def test_fixed_references_score_1000_and_twice_as_fast_scores_2000(self):
        self.assertAlmostEqual(1000, performance.score(self.metrics()))
        self.assertAlmostEqual(2000, performance.score(self.metrics(.5)))
        self.assertAlmostEqual(500, performance.score(self.metrics(2)))

    def test_slow_tail_reduces_score_even_when_median_unchanged(self):
        metrics = self.metrics()
        reference = performance.METRICS["startup.files"][0]
        metrics["startup.files"] = performance.summarize([reference] * 6 + [reference * 10])
        self.assertLess(performance.score(metrics), 1000)
        self.assertEqual(reference, metrics["startup.files"]["medianMs"])

    def test_percentile_median_and_mad_are_correct(self):
        summary = performance.summarize([7, 1, 6, 2, 5, 3, 4])
        self.assertEqual(4, summary["medianMs"])
        self.assertEqual(7, summary["p95Ms"])
        self.assertEqual(2, summary["madMs"])

    def test_missing_metric_cannot_improve_score(self):
        metrics = self.metrics()
        del metrics["startup.settings"]
        with self.assertRaises(ValueError):
            performance.score(metrics)

    def test_failed_zero_nan_or_missing_samples_are_rejected(self):
        for values in ([], [0], [-1], [math.nan], [math.inf]):
            with self.subTest(values=values), self.assertRaises(ValueError):
                performance.summarize(values)

    def test_comparison_recomputes_summaries_and_requires_matching_environment(self):
        before, after = self.report(), self.report(.5)
        after["metrics"]["startup.files"]["effectiveMs"] = 1e-12  # Not trusted.
        with contextlib.redirect_stdout(io.StringIO()):
            self.assertAlmostEqual(2, performance.compare_reports(before, after))
        after["environment"]["comparable"]["configuration"] = "Debug"
        with self.assertRaisesRegex(ValueError, "Environment differs"):
            performance.compare_reports(before, after)
        with contextlib.redirect_stdout(io.StringIO()) as console:
            performance.compare_reports(before, after, True)
        self.assertIn("QUALIFIED COMPARISON", console.getvalue())

    def test_startup_tracing_must_match_and_legacy_reports_default_to_disabled(self):
        before, after = self.report(), self.report()
        after["startupTraceEnabled"] = False
        with contextlib.redirect_stdout(io.StringIO()):
            self.assertEqual(1, performance.compare_reports(before, after))
        after["startupTraceEnabled"] = True
        with self.assertRaisesRegex(ValueError, "startup tracing"):
            performance.compare_reports(before, after, True)

    def test_incomplete_or_incompatible_reports_are_rejected(self):
        for field, value in (("status", "failed"), ("protocol", "v2"), ("fixtureHash", "different"), ("samples", 30), ("warmups", 0)):
            before, after = self.report(), self.report()
            after[field] = value
            with self.subTest(field=field), self.assertRaises(ValueError):
                performance.compare_reports(before, after)
        after = self.report()
        after["metrics"]["files.sort"]["rawMs"].pop()
        with self.assertRaisesRegex(ValueError, "sample counts"):
            performance.compare_reports(self.report(), after)

    def test_modified_scoring_weights_are_rejected(self):
        before, after = self.report(), self.report()
        after["metrics"]["files.sort"]["weight"] = 100
        with self.assertRaisesRegex(ValueError, "weights"):
            performance.compare_reports(before, after)

    def test_stale_or_wrong_process_readiness_signals_are_rejected(self):
        signal = {"protocol": 1, "token": "nonce", "processId": 123, "application": "files", "stage": "initial-content-rendered"}
        performance.validate_signal(signal, 123, "nonce", "files")
        for field, value in (("protocol", 2), ("token", "stale"), ("processId", 124), ("application", "settings"), ("stage", "failed")):
            changed = signal | {field: value}
            with self.subTest(field=field), self.assertRaises(ValueError):
                performance.validate_signal(changed, 123, "nonce", "files")

    def test_fixtures_have_exact_counts_stable_contents_and_hash(self):
        with tempfile.TemporaryDirectory() as a, tempfile.TemporaryDirectory() as b:
            a, b = Path(a), Path(b)
            self.assertEqual(performance.fixtures(a), performance.fixtures(b))
            self.assertEqual(1000, len(list((a / "files").iterdir())))
            self.assertEqual(20, len(list((a / "notes").iterdir())))
            for source in (a / "files").iterdir():
                self.assertEqual(source.read_bytes(), (b / "files" / source.name).read_bytes())
                self.assertEqual(1577836800, source.stat().st_mtime)


if __name__ == "__main__":
    unittest.main()
