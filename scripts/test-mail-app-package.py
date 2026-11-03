#!/usr/bin/env python3
"""Validate Mail installation using an isolated home and a harmless executable."""
import os
from pathlib import Path
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]

class MailPackageTests(unittest.TestCase):
    def test_launcher_icons_uri_handler_and_pin(self):
        with tempfile.TemporaryDirectory(prefix='daisy-mail-package-') as directory:
            root = Path(directory)
            home, data, config = root / 'home', root / 'data', root / 'config'
            home.mkdir(); data.mkdir(); config.mkdir()
            binary = root / 'Mail build $ literal' / 'DaisyOS.Mail'
            binary.parent.mkdir()
            binary.write_text('#!/usr/bin/env bash\nprintf "%s\\n" "$@" > "$HOME/invocation"\n')
            binary.chmod(0o755)
            env = dict(os.environ, HOME=str(home), XDG_DATA_HOME=str(data), XDG_CONFIG_HOME=str(config),
                       DBUS_SESSION_BUS_ADDRESS='', WAYLAND_DISPLAY='', DISPLAY='', XDG_CURRENT_DESKTOP='', DESKTOP_SESSION='')
            subprocess.run([str(ROOT / 'scripts/install-mail-app.sh'), str(binary), '--pin'], env=env, check=True, capture_output=True, text=True)
            entry = data / 'applications/org.daisyos.mail.desktop'
            text = entry.read_text()
            self.assertIn('Exec="', text)
            self.assertIn('" %u\n', text)
            self.assertIn('MimeType=x-scheme-handler/mailto;', text)
            self.assertIn('StartupWMClass=org.daisyos.mail', text)
            self.assertEqual(binary, (home / '.local/bin/daisyos-mail').resolve())
            self.assertEqual('--pin-to-taskbar\n', (home / 'invocation').read_text())
            for size in (256, 1024):
                self.assertTrue((data / f'icons/hicolor/{size}x{size}/apps/org.daisyos.mail.png').is_file())
            if subprocess.run(['sh', '-c', 'command -v desktop-file-validate'], capture_output=True).returncode == 0:
                subprocess.run(['desktop-file-validate', str(entry)], check=True)
            # Installing Mail must preserve an already chosen mail client.
            other = data / 'applications/existing-mail.desktop'
            other.write_text('[Desktop Entry]\nType=Application\nName=Existing mail\nExec=/usr/bin/true %u\nMimeType=x-scheme-handler/mailto;\n')
            (config / 'mimeapps.list').write_text('[Default Applications]\nx-scheme-handler/mailto=existing-mail.desktop\n')
            subprocess.run([str(ROOT / 'scripts/install-mail-app.sh'), str(binary)], env=env, check=True, capture_output=True)
            self.assertIn('x-scheme-handler/mailto=existing-mail.desktop', (config / 'mimeapps.list').read_text())

if __name__ == '__main__':
    unittest.main()
