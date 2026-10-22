#!/usr/bin/env python3
import importlib.util
from pathlib import Path
import unittest
import os
import subprocess
import tempfile
import sys

sys.dont_write_bytecode = True

spec = importlib.util.spec_from_file_location('installer', Path(__file__).with_name('install-user-gaming-integration.py'))
installer = importlib.util.module_from_spec(spec)
spec.loader.exec_module(installer)


class DesktopTests(unittest.TestCase):
    def test_preserves_localized_names_actions_and_arguments(self):
        source = '[Desktop Entry]\nName=Steam\nName[fi]=Steam\nExec=/usr/bin/steam %U\nDBusActivatable=true\nActions=Library;\n[Desktop Action Library]\nName=Library\nExec=/usr/bin/steam steam://open/games\n'
        result = installer.instrument(source, Path('/home/user/.local/bin/daisyos-game-launcher'))
        self.assertIn('Name[fi]=Steam', result)
        self.assertIn('DBusActivatable=false', result)
        self.assertIn('" /usr/bin/steam %U', result)
        self.assertIn('" /usr/bin/steam steam://open/games', result)
        self.assertEqual(result, installer.instrument(result, Path('/home/user/.local/bin/daisyos-game-launcher')))

    def test_inserts_dbus_override_in_the_main_section_only(self):
        result = installer.instrument('[Desktop Entry]\nName=Game\nExec=game\n[Desktop Action Test]\nExec=game --test\n', Path('/tmp/wrapper'))
        self.assertIn('DBusActivatable=false\n[Desktop Action Test]', result)
        self.assertEqual(1, result.count('DBusActivatable='))

    def test_quotes_paths_with_spaces_and_field_codes(self):
        self.assertEqual('"/tmp/my games/100%%/wrapper"', installer.quote_exec(Path('/tmp/my games/100%/wrapper')))

    def test_install_preserves_existing_override_and_is_idempotent(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            applications = root / 'applications'
            applications.mkdir()
            target = applications / 'steam.desktop'
            original = '[Desktop Entry]\nName=My Steam\nExec=env CUSTOM=value /usr/bin/steam %U\n'
            target.write_text(original)
            command = ['python3', str(Path(__file__).with_name('install-user-gaming-integration.py')),
                       '--bin-dir', str(root / 'bin'), '--applications-dir', str(applications)]
            env = dict(os.environ, XDG_DATA_DIRS=str(root / 'empty'))
            subprocess.run(command, env=env, check=True, capture_output=True)
            first = target.read_text()
            subprocess.run(command, env=env, check=True, capture_output=True)
            self.assertEqual(first, target.read_text())
            self.assertIn('env CUSTOM=value /usr/bin/steam %U', first)
            self.assertEqual(original, target.with_name('steam.desktop.before-daisyos-telemetry').read_text())
            self.assertTrue(os.access(root / 'bin/daisyos-game-launcher', os.X_OK))


if __name__ == '__main__':
    unittest.main()
