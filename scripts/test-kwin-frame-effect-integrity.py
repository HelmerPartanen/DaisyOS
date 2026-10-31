#!/usr/bin/env python3
"""Check frame-effect upgrade detection using isolated metadata and shader files."""
import hashlib
import os
from pathlib import Path
import re
import subprocess
import tempfile

root = Path(__file__).resolve().parents[1]
installer = root / 'scripts/install-kwin-frame-effect.sh'
source = installer.read_text()
plugin = Path.home() / '.local/lib/qt6/plugins/kwin/effects/plugins/kwin4_effect_shapecorners.so'
assert plugin.is_file(), 'Install the frame effect before running its integrity test'
version = subprocess.check_output(['kwin_wayland', '--version'], stderr=subprocess.DEVNULL, text=True).strip()
commits = [re.search(r'^' + name + r'="([a-f0-9]+)"', source, re.M)[1]
           for name in ('effect_commit', 'ecm_commit', 'vulkan_headers_commit')]
def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()
fingerprint = hashlib.sha256(('\n'.join(commits + [version, digest(installer),
    digest(root / 'src/native/kwin_frame_effect/daisyos-layer-surfaces.patch')]) + '\n').encode()).hexdigest()
with tempfile.TemporaryDirectory(prefix='daisy-frame-integrity-') as temporary:
    data = Path(temporary)
    shaders = data / 'kwin/shaders'
    shaders.mkdir(parents=True)
    files = [plugin, shaders / 'shapecorners.frag', shaders / 'shapecorners_core.frag']
    for shader in files[1:]:
        shader.write_text('fixture shader\n')
    stamp = data / 'daisyos/kwin-frame-effect.version'
    stamp.parent.mkdir()
    stamp.write_text(fingerprint + '\n')
    manifest = stamp.with_name(stamp.name + '.sha256')
    manifest.write_text(''.join(f'{digest(path)}  {path}\n' for path in files))
    env = dict(os.environ, XDG_DATA_HOME=temporary)
    def check(expected):
        result = subprocess.run([str(installer), '--check'], env=env)
        assert (result.returncode == 0) == expected, result.returncode
    check(True)
    files[1].write_text('stale shader\n')
    check(False)
    files[1].write_text('fixture shader\n')
    check(True)
    files[2].unlink()
    check(False)
    files[2].write_text('fixture shader\n')
    manifest.unlink()
    check(False)
    print('PASS: valid install accepted; changed shaders, missing shaders and missing integrity metadata require repair')
