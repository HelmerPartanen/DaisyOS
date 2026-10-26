#!/usr/bin/env python3
import os
from pathlib import Path
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]


class AutomaticGamingTests(unittest.TestCase):
    def test_clean_image_stages_launchers_permissions_and_policy(self):
        with tempfile.TemporaryDirectory() as temporary:
            stage = Path(temporary)
            subprocess.run([str(ROOT / 'scripts/stage-gaming-integration.sh'), '--root', str(stage)], check=True)
            for name in ('daisyos-game', 'daisyos-game-launcher', 'steam', 'lutris', 'wine'):
                self.assertTrue(os.access(stage / 'usr/local/bin' / name, os.X_OK))
            for name in ('steam', 'lutris'):
                desktop_id = 'net.lutris.Lutris' if name == 'lutris' else name
                desktop = (stage / f'usr/local/share/applications/{desktop_id}.desktop').read_text()
                self.assertIn(f'Exec=/usr/local/bin/daisyos-game-launcher /usr/bin/{name} %U', desktop)
                self.assertIn('DBusActivatable=false', desktop)
            wine_desktop = (stage / 'usr/local/share/applications/wine.desktop').read_text()
            self.assertIn('Exec=/usr/local/bin/wine start /unix %f', wine_desktop)
            self.assertIn('MimeType=application/x-ms-dos-executable;', wine_desktop)
            self.assertIn('NoDisplay=true', wine_desktop)
            policy = (stage / 'usr/share/polkit-1/rules.d/60-daisyos-gamemode.rules').read_text()
            self.assertIn('subject.local === true && subject.active === true', policy)
            self.assertIn('action.id === "com.feralinteractive.GameMode.governor-helper"', policy)
            self.assertNotIn('gpu-helper', policy)

    def test_session_instruments_vulkan_without_global_opengl_preload(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            binary = root / 'mangohud'
            binary.write_text('#!/bin/sh\nexit 0\n'); binary.chmod(0o755)
            env = dict(os.environ, PATH=f'{root}:/usr/bin:/bin', XDG_RUNTIME_DIR=str(root), USER='fresh-user')
            for name in ('MANGOHUD_CONFIG', 'LD_PRELOAD', 'DAISYOS_MANGOHUD_LOG_DIR'):
                env.pop(name, None)
            result = subprocess.check_output(['bash', '-c', 'source "$1"; source "$1"; printf "%s\\n%s\\n%s\\n" "$MANGOHUD" "$MANGOHUD_CONFIG" "${LD_PRELOAD:-empty}"',
                                              'bash', str(ROOT / 'scripts/daisyos-gaming-environment')], env=env, text=True)
            self.assertTrue(result.startswith('1\n'))
            self.assertIn('control=daisyos-fps-fresh-user-%p', result)
            self.assertEqual(1, result.count('control='))
            self.assertIn('no_display,autostart_log=0,log_interval=1000', result)
            self.assertTrue(result.endswith('empty\n'))
            self.assertTrue((root / 'daisyos/gaming-telemetry').is_dir())

    def test_store_tree_gets_gl_shim_and_preserves_launch_arguments(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            binary = root / 'mangohud'
            binary.write_text('#!/bin/sh\nexit 0\n'); binary.chmod(0o755)
            env = dict(os.environ, PATH=f'{root}:/usr/bin:/bin', XDG_RUNTIME_DIR=str(root), USER='fresh-user',
                       DAISYOS_GAMING_ENVIRONMENT=str(ROOT / 'scripts/daisyos-gaming-environment'))
            env.pop('MANGOHUD_CONFIG', None); env.pop('LD_PRELOAD', None)
            result = subprocess.run([str(ROOT / 'scripts/daisyos-game-launcher'), '/usr/bin/env'],
                                    env=env, text=True, capture_output=True, check=True)
            self.assertIn('LD_PRELOAD=/usr/$LIB/mangohud/libMangoHud_shim.so', result.stdout)
            self.assertIn('control=daisyos-fps-fresh-user-%p', result.stdout)


if __name__ == '__main__':
    unittest.main()
