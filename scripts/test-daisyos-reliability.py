#!/usr/bin/env python3
"""Unit and isolated integration tests for the DaisyOS reliability harness."""

import importlib.util
import json
from pathlib import Path
import tempfile
import unittest


spec = importlib.util.spec_from_file_location("reliability", Path(__file__).with_name("daisyos-reliability.py"))
reliability = importlib.util.module_from_spec(spec)
spec.loader.exec_module(reliability)


class ReliabilityTests(unittest.TestCase):
    def test_classifier_covers_failure_families_and_preserves_basis(self):
        cases = {
            "kernel-oom": "kernel: Out of memory: Killed process 42 (browser)",
            "systemd-oomd": "systemd-oomd: Killed /user.slice/app.scope due to memory pressure",
            "application-fault": "app[8]: segfault at 0 ip 0000",
            "application-abort": "app: free(): invalid pointer",
            "gpu-driver": "amdgpu: GPU reset begin!",
            "storage-io": "kernel: Buffer I/O error on dev nvme0n1",
            "thermal-power": "thermal: critical temperature reached, shutting down",
            "explicit-signal": "service: Main process exited, status=15/TERM",
            "supervisor-action": "daisyos-session: shell stopped unexpectedly; retry 1",
        }
        for expected, line in cases.items():
            with self.subTest(expected=expected):
                event = reliability.classify_text(line)[0]
                self.assertEqual(expected, event["category"])
                self.assertIn(event["basis"], ("measured", "inferred"))

    def test_policy_warning_and_benign_assertion_are_not_crashes(self):
        warning = reliability.classify_text("systemd-oomd: No swap; memory pressure usage will be degraded")
        self.assertEqual("memory-policy-warning", warning[0]["category"])
        self.assertEqual([], reliability.classify_text("helper: assertion 'max > min' failed"))
        self.assertEqual([], reliability.classify_text("MCE: In-kernel MCE decoding enabled"))

    def test_classifier_handles_malformed_and_redacts_sensitive_values(self):
        self.assertEqual([], reliability.classify_text("not a journal\n\x00\n"))
        event = reliability.classify_text("app: segfault user@example.com token=abc123")[0]
        self.assertNotIn("user@example.com", event["evidence"])
        self.assertNotIn("abc123", event["evidence"])

    def test_coredump_metadata_classifies_signals_without_assuming_kill_cause(self):
        text = "\n".join((json.dumps({"COREDUMP_PID": "7", "COREDUMP_EXE": "/usr/bin/app", "SIGNAL": "11"}),
                           json.dumps({"COREDUMP_PID": "8", "SIGNAL": "9"}), "malformed"))
        events = reliability.classify_coredumps(text)
        self.assertEqual(["application-fault", "unexplained-signal"], [item["category"] for item in events])
        self.assertEqual("7", events[0]["evidence"]["pid"])

    def test_empty_coredump_listing_is_valid_evidence(self):
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp) / "coredumps.json"
            path.write_text("No coredumps found.\n")
            self.assertFalse(reliability.collection_failed({"exitCode": 1}, path))

    def test_manifest_rejects_shell_strings_and_unknown_steps(self):
        valid = {"protocol": reliability.PROTOCOL, "version": 1,
                 "profiles": [{"id": "test", "steps": [{"type": "command", "argv": ["true"]}]}]}
        reliability.validate_manifest(valid)
        invalid = json.loads(json.dumps(valid)); invalid["profiles"][0]["steps"][0]["argv"] = "true"
        with self.assertRaises(ValueError):
            reliability.validate_manifest(invalid)
        invalid = json.loads(json.dumps(valid)); invalid["profiles"][0]["steps"][0]["type"] = "shell"
        with self.assertRaises(ValueError):
            reliability.validate_manifest(invalid)

    def make_run(self, root, journal="", available=2 * 1024 ** 3, full=0, workload_status="passed"):
        run = root / "run"
        raw = run / "raw"; raw.mkdir(parents=True)
        for name in reliability.MANDATORY_RAW:
            (raw / name).write_text(journal if "journal" in name else "[]\n")
        sample = {"memAvailableBytes": available, "usedDefinitionBytes": 1024 ** 3,
                  "psiMemory": {"full": {"avg10": full}}, "topProcesses": [], "kernel": {}}
        (run / "samples.jsonl").write_text(json.dumps(sample) + "\n")
        (run / "workload-results.jsonl").write_text(json.dumps({"status": workload_status}) + "\n")
        return run

    def test_summary_passes_clean_evidence_and_fails_oom(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            run = self.make_run(root)
            report, status = reliability.summarize_run(run)
            self.assertEqual("passed", report["status"]); self.assertEqual(0, status)
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            run = self.make_run(root, "kernel: Out of memory: Killed process 9")
            report, status = reliability.summarize_run(run)
            self.assertEqual("failed", report["status"]); self.assertEqual(1, status)
            self.assertIn("OOM kill observed", report["failures"])

    def test_summary_fails_pressure_missing_evidence_and_workload(self):
        with tempfile.TemporaryDirectory() as temp:
            run = self.make_run(Path(temp), available=512 * 1024 ** 2, full=.2, workload_status="failed")
            (run / "raw" / "coredumps.json").unlink()
            report, status = reliability.summarize_run(run)
            self.assertEqual(1, status)
            self.assertGreaterEqual(len(report["failures"]), 4)

    def test_forced_signal_is_classified_but_not_an_unexplained_crash(self):
        with tempfile.TemporaryDirectory() as temp:
            run = self.make_run(Path(temp), "service: Main process exited, status=15/TERM")
            report, status = reliability.summarize_run(run)
            self.assertEqual(0, status)
            self.assertEqual(1, report["eventCounts"]["explicit-signal"])

    def test_command_failures_include_missing_tool_and_profiler(self):
        missing = reliability.command_result(["definitely-not-a-real-daisyos-command"])
        self.assertEqual("command-not-found", missing["error"])
        with tempfile.TemporaryDirectory() as temp:
            run = Path(temp); (run / "workload").mkdir(); (run / "fixtures").mkdir()
            result = reliability.execute_step({"type": "command", "name": "missing", "argv": ["definitely-missing"]}, run, 1)
            self.assertEqual("failed", result["status"])
            result = reliability.execute_step({"type": "command", "name": "profile", "argv": ["true"]}, run, 2, "heaptrack")
            if not reliability.shutil.which("heaptrack"):
                self.assertEqual("failed", result["status"])

    def test_compare_reports_deltas_without_claiming_causality(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            before = {"protocol": reliability.PROTOCOL, "status": "failed", "sampleCount": 2,
                      "comparisonIdentity": {"host": "test", "manifestHash": "same"},
                      "minimumMemAvailableBytes": 10, "maximumUsedDefinitionBytes": 20,
                      "maximumPsiFullAvg10": 1.0, "eventCounts": {"kernel-oom": 1}}
            after = before | {"status": "passed", "minimumMemAvailableBytes": 30,
                              "maximumUsedDefinitionBytes": 15, "maximumPsiFullAvg10": 0,
                              "eventCounts": {}}
            (root / "before.json").write_text(json.dumps(before)); (root / "after.json").write_text(json.dumps(after))
            result = reliability.compare_runs(root / "before.json", root / "after.json", root / "comparison.json")
            self.assertEqual(20, result["deltas"]["minimumMemAvailableBytes"])
            self.assertEqual(-1, result["eventCountChanges"]["kernel-oom"])
            self.assertTrue(result["qualified"])
            changed = dict(after); changed["comparisonIdentity"] = {"host": "other", "manifestHash": "same"}
            (root / "changed.json").write_text(json.dumps(changed))
            with self.assertRaisesRegex(ValueError, "identity differs"):
                reliability.compare_runs(root / "before.json", root / "changed.json", root / "bad.json")

    def test_growth_detection_requires_sustained_warmed_growth(self):
        samples = []
        for index, value in enumerate((100, 102, 106, 112, 118, 125)):
            samples.append({"topProcesses": [{"pid": 42, "startTicks": 10, "command": "leaker", "pssBytes": value * reliability.MIB}]})
        candidates = reliability.detect_growth(samples)
        self.assertEqual("leaker", candidates[0]["command"])
        flat = [{"topProcesses": [{"pid": 42, "startTicks": 10, "command": "stable", "pssBytes": 100 * reliability.MIB}]} for _ in range(6)]
        self.assertEqual([], reliability.detect_growth(flat))

    def test_pid_reuse_does_not_look_like_growth(self):
        samples = []
        for index in range(6):
            samples.append({"topProcesses": [{"pid": 42, "startTicks": index // 3, "command": "worker",
                                               "pssBytes": (100 + index * 20) * reliability.MIB}]})
        self.assertEqual([], reliability.detect_growth(samples))


if __name__ == "__main__":
    unittest.main()
