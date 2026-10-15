#!/usr/bin/env python3
import os
from pathlib import Path
import subprocess
import tempfile
import unittest

LAUNCHER = Path(__file__).with_name('daisyos-game').resolve()


class LauncherTests(unittest.TestCase):
    def test_starts_hidden_logging_and_preserves_arguments_and_existing_settings(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            fake = root / 'mangohud'
            fake.write_text("""#!/usr/bin/python3
import os, socket, sys
config = os.environ['MANGOHUD_CONFIG']
print(config)
for value in sys.argv[1:]: print('arg=' + value)
name = next(option.split('=', 1)[1] for option in config.split(',') if option.startswith('control='))
with socket.socket(socket.AF_UNIX) as server:
    server.bind('\\0' + name)
    server.listen(1)
    server.settimeout(5)
    peer, _ = server.accept()
    with peer:
        peer.sendall(b':MangoHudControlVersion=1;')
        print('control=' + peer.recv(1024).decode())
""")
            fake.chmod(0o755)
            env = dict(os.environ, PATH=str(root) + ':/usr/bin:/bin', XDG_RUNTIME_DIR=str(root), MANGOHUD_CONFIG='fps_limit=90')
            env.pop('DAISYOS_MANGOHUD_LOG_DIR', None)
            output = subprocess.check_output([str(LAUNCHER), 'game', 'argument with spaces', '$(literal)'], env=env, text=True)
            self.assertIn('fps_limit=90,read_cfg=0,no_display,autostart_log=0,log_interval=1000', output)
            self.assertIn('output_folder=' + str(root / 'daisyos/gaming-telemetry'), output)
            self.assertIn('arg=argument with spaces\narg=$(literal)', output)
            self.assertTrue((root / 'daisyos/gaming-telemetry').is_dir())
            self.assertIn('control=:logging=1;', output)

    def test_missing_command_is_an_error(self):
        self.assertEqual(2, subprocess.run([str(LAUNCHER)], capture_output=True).returncode)


if __name__ == '__main__':
    unittest.main()
