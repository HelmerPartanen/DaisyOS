#!/usr/bin/env python3
"""Exercise screen-sharing repair without touching host services or settings."""
import os
from pathlib import Path
import socket
import subprocess
import tempfile
import unittest

SCRIPT = Path(__file__).resolve().parent / 'prepare-screen-sharing.sh'


class ScreenSharingSetupTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='daisy-portals-')
        self.root = Path(self.temp.name)
        self.bin = self.root / 'bin'
        self.bin.mkdir()
        self.runtime = self.root / 'runtime'
        self.runtime.mkdir()
        self.display = socket.socket(socket.AF_UNIX)
        self.display.bind(str(self.runtime / 'wayland-test'))
        self.env = dict(os.environ, PATH=f'{self.bin}:{os.environ["PATH"]}',
                        XDG_CONFIG_HOME=str(self.root / 'config'),
                        XDG_RUNTIME_DIR=str(self.runtime), WAYLAND_DISPLAY='wayland-test',
                        XDG_CURRENT_DESKTOP='DaisyOS', XDG_SESSION_TYPE='wayland',
                        DAISYOS_TEST_FIXTURE=str(self.root))
        self.tool('systemctl', '''printf '%s\\n' "$*" >> "$DAISYOS_TEST_FIXTURE/calls"
if [[ "$*" == '--user restart plasma-xdg-desktop-portal-kde.service' ]]; then
 touch "$DAISYOS_TEST_FIXTURE/backend-ready"
fi
if [[ "$*" == '--user restart xdg-desktop-portal.service' ]]; then
 touch "$DAISYOS_TEST_FIXTURE/frontend-ready"
fi
''')
        self.tool('busctl', '''if [[ "${DAISYOS_TEST_ALWAYS_EMPTY:-0}" == 1 ]]; then echo 'u 0'; exit 0; fi
case "$*" in
 *org.freedesktop.impl.portal.desktop.kde*) marker=backend-ready ;;
 *) marker=frontend-ready ;;
esac
if [[ -e "$DAISYOS_TEST_FIXTURE/$marker" ]]; then echo 'u 7'; else echo 'u 0'; fi
''')

    def tearDown(self):
        self.display.close()
        self.temp.cleanup()

    def tool(self, name, body):
        path = self.bin / name
        path.write_text('#!/usr/bin/env bash\nset -euo pipefail\n' + body)
        path.chmod(0o755)

    def run_setup(self, **overrides):
        return subprocess.run([str(SCRIPT)], env=dict(self.env, **overrides),
                              text=True, capture_output=True, timeout=15)

    def test_repairs_missing_interfaces_and_preserves_working_streams_on_repeat(self):
        result = self.run_setup()
        self.assertEqual(result.returncode, 0, result.stderr)
        calls = (self.root / 'calls').read_text()
        self.assertIn('--user restart plasma-xdg-desktop-portal-kde.service', calls)
        self.assertIn('--user restart xdg-desktop-portal.service', calls)
        config = self.root / 'config/systemd/user/plasma-xdg-desktop-portal-kde.service.d/50-daisyos-capture.conf'
        self.assertIn('Environment=XDG_CURRENT_DESKTOP=KDE', config.read_text())
        self.assertIn('Environment=QT_QPA_PLATFORM=wayland', config.read_text())
        result = self.run_setup()
        self.assertEqual(result.returncode, 0, result.stderr)
        added_calls = (self.root / 'calls').read_text()[len(calls):]
        self.assertNotIn('restart', added_calls)
        self.assertNotIn('daemon-reload', added_calls)
        self.assertNotIn('pipewire', calls)

    def test_refuses_other_desktops_or_missing_wayland_before_touching_services(self):
        for overrides in ({'XDG_CURRENT_DESKTOP': 'GNOME'}, {'WAYLAND_DISPLAY': 'missing'}):
            result = self.run_setup(**overrides)
            self.assertNotEqual(result.returncode, 0)
            self.assertFalse((self.root / 'calls').exists())
            self.assertFalse((self.root / 'config').exists())

    def test_reports_failure_if_restart_cannot_restore_monitor_capture(self):
        result = self.run_setup(DAISYOS_TEST_ALWAYS_EMPTY='1')
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('does not expose monitor capture', result.stderr)


if __name__ == '__main__':
    unittest.main()
