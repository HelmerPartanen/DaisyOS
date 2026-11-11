#!/usr/bin/env python3
import importlib.util
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location("compare_runs", Path(__file__).with_name("compare-desktop-session-runs.py"))
compare_runs = importlib.util.module_from_spec(spec)
spec.loader.exec_module(compare_runs)


def report(pss=600, complete=True):
    return {"metadata": {"protocol": compare_runs.PROTOCOL, "host": "host", "kernel": "kernel",
                         "machine": "x86_64", "sessionType": "wayland", "desktop": "DaisyOS",
                         "selection": {}},
            "workload": {"state": "fresh-login-idle", "annotations": {"buildId": "release-1"}},
            "summary": {"readinessLatencyMs": 500},
            "samples": [{"elapsedSeconds": 300, "desktopProcessesPssBytes": pss * 1024 * 1024,
                          "desktopProcessesPssComplete": complete,
                          "desktopProcessesPrivateBytes": 400 * 1024 * 1024,
                          "processesWithoutReadableSmaps": 0 if complete else 1,
                          "memory": {"usedExcludingAvailableBytes": 2 * 1024 * 1024 * 1024,
                                     "availableBytes": 2 * 1024 * 1024 * 1024},
                          "desktopProcesses": [{"cpuPercentOfOneCore": 1.0}]}]}


class CompareDesktopSessionRunsTests(unittest.TestCase):
    def test_reports_median_spread_and_budget(self):
        with tempfile.TemporaryDirectory() as directory:
            paths = []
            for index, pss in enumerate((600, 610, 620)):
                path = Path(directory) / f"run-{index}.json"
                path.write_text(__import__("json").dumps(report(pss)))
                paths.append(path)
            result = compare_runs.compare(paths)
        self.assertTrue(result["acceptanceEligible"])
        self.assertTrue(result["idlePssBudgetPassed"])
        self.assertEqual(result["idlePssMedianMiB"], 610)
        self.assertEqual(result["metrics"]["desktopProcessesPssMiB"]["mad"], 10)

    def test_incomplete_pss_cannot_pass_budget(self):
        with tempfile.TemporaryDirectory() as directory:
            paths = []
            for index in range(3):
                path = Path(directory) / f"run-{index}.json"
                path.write_text(__import__("json").dumps(report(200, complete=index != 1)))
                paths.append(path)
            result = compare_runs.compare(paths)
        self.assertFalse(result["acceptanceEligible"])
        self.assertFalse(result["idlePssBudgetPassed"])


if __name__ == "__main__":
    unittest.main()
