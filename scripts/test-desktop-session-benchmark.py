#!/usr/bin/env python3
import importlib.util
from datetime import datetime, timezone
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location("desktop_session", Path(__file__).with_name("benchmark-desktop-session.py"))
desktop_session = importlib.util.module_from_spec(spec)
spec.loader.exec_module(desktop_session)


class DesktopSessionBenchmarkTests(unittest.TestCase):
    def test_process_roles_cover_shell_compositor_and_services(self):
        self.assertEqual(desktop_session.classify_process("DaisyOS.Shell", "DaisyOS.Shell --real-services"), "shell")
        self.assertIsNone(desktop_session.classify_process("DaisyOS.Shell", "DaisyOS.Shell --notes"))
        self.assertEqual(desktop_session.classify_process("kwin_wayland", "kwin_wayland --drm"), "compositor")
        self.assertEqual(desktop_session.classify_process("DaisyOS.Continuity", "DaisyOS.Continuity"), "daisyos-service")
        self.assertEqual(desktop_session.classify_process("DaisyOS.Continu", "/opt/DaisyOS.Continuity"), "daisyos-service")
        self.assertEqual(desktop_session.classify_process("DaisyOS.Shell", "DaisyOS.Shell --desktop-services"), "daisyos-service")
        self.assertEqual(desktop_session.classify_process("pipewire", "pipewire"), "desktop-platform")
        self.assertIsNone(desktop_session.classify_process("code", "code --no-sandbox"))

    def test_summary_keeps_missing_measurements_explicit(self):
        sample = {"desktopProcessesPssBytes": 100 * desktop_session.MIB,
                  "desktopProcessesPssComplete": True,
                  "desktopProcessesPrivateBytes": 80 * desktop_session.MIB,
                  "memory": {"usedExcludingAvailableBytes": 500 * desktop_session.MIB,
                             "availableBytes": 1500 * desktop_session.MIB},
                  "desktopProcesses": [{"role": "service", "cpuPercentOfOneCore": 0.5}]}
        result = desktop_session.summarize([sample])
        self.assertEqual(result["medianDesktopProcessesPssMiB"], 100)
        self.assertEqual(result["medianDesktopCpuPercentOfOneCore"], 0.5)
        self.assertIsNone(result["readinessLatencyMs"])
        self.assertIsNone(result["interactionLatenciesMs"])

    def test_readiness_uses_render_marker_and_process_start(self):
        rendered = datetime.now(timezone.utc).isoformat()
        rendered_epoch = datetime.fromisoformat(rendered).timestamp()
        pid = 54321
        with tempfile.TemporaryDirectory() as directory:
            log = Path(directory) / "session.log"
            log.write_text(f"[daisy-shell-ready] pid={pid} utc={rendered} stage=initial-desktop-rendered\n")
            sample = {"desktopProcessesPssBytes": 1, "desktopProcessesPrivateBytes": 1,
                      "desktopProcessesPssComplete": True,
                      "memory": {"usedExcludingAvailableBytes": 1, "availableBytes": 1},
                      "desktopProcesses": [{"pid": pid, "role": "shell",
                                            "processStartEpochSeconds": rendered_epoch - 1.25,
                                            "cpuPercentOfOneCore": 0.0}]}
            result = desktop_session.summarize([sample], log)
        self.assertEqual(result["readinessLatencyMs"], 1250)
        self.assertIn("composition batch", result["readinessSource"])


if __name__ == "__main__":
    unittest.main()
