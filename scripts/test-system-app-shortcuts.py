#!/usr/bin/env python3
"""Regression checks for managed desktop shortcut migration."""
import os
from pathlib import Path
import subprocess
import tempfile
import unittest

SCRIPT = Path(__file__).with_name('update-system-app-shortcuts.py')

class ShortcutTests(unittest.TestCase):
    def test_only_managed_exec_changes_and_user_metadata_survives(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp); repo = root / 'checkout with spaces'; home = root / 'home'
            desktop = home / 'Desktop'; desktop.mkdir(parents=True)
            for app in ('Files', 'Notes', 'Settings'):
                for config in ('Debug', 'Release'):
                    target = repo / 'src' / f'DaisyOS.{app}' / 'bin' / config / 'net10.0' / f'DaisyOS.{app}'
                    target.parent.mkdir(parents=True, exist_ok=True); target.touch()
            old = repo / 'src/DaisyOS.Files/bin/Debug/net10.0/DaisyOS.Files'
            files = desktop / 'org.daisyos.files.desktop'
            files.write_text(f'[Desktop Entry]\nName=My Files\nExec="{old}" %U\nIcon=my-icon\n')
            notes = desktop / 'org.daisyos.notes.desktop'
            custom = '[Desktop Entry]\nName=My Notes\nExec=/usr/bin/custom-editor --debug\n'
            notes.write_text(custom)
            env = {**os.environ, 'HOME': str(home), 'XDG_CONFIG_HOME': str(home / '.config')}
            subprocess.run(['python3', str(SCRIPT), str(repo), 'Release'], env=env, check=True, capture_output=True)
            self.assertIn('/bin/Release/', files.read_text())
            self.assertIn('Name=My Files\n', files.read_text())
            self.assertIn('Icon=my-icon\n', files.read_text())
            self.assertEqual(custom, notes.read_text())
            subprocess.run(['python3', str(SCRIPT), str(repo), 'Debug'], env=env, check=True, capture_output=True)
            self.assertIn('/bin/Debug/', files.read_text())

if __name__ == '__main__': unittest.main()
