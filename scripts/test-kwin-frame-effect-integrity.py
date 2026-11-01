#!/usr/bin/env python3
"""Verify upgrade detection without installing into the user's session."""
import hashlib
import os
from pathlib import Path
import re
import subprocess
import tempfile

root = Path(__file__).resolve().parents[1]
installer = root / 'scripts/install-kwin-frame-effect.sh'
source = installer.read_text()
commits = [re.search(r'^' + name + r'="([a-f0-9]+)"', source, re.M)[1]
           for name in ('ecm_commit', 'vulkan_headers_commit')]
def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()
inputs = [installer] + [root / 'src/native/kwin_frame_effect' / name
    for name in ('CMakeLists.txt', 'frameeffect.cpp', 'frame.frag', 'metadata.json', 'resources.qrc')]
version = 'kwin 6.8.1'
fingerprint = hashlib.sha256(('\n'.join(commits + [version] + [digest(p) for p in inputs]) + '\n').encode()).hexdigest()
with tempfile.TemporaryDirectory(prefix='daisy-frame-integrity-') as temporary:
    home = Path(temporary)
    plugin = home / '.local/lib/qt6/plugins/kwin/effects/plugins/daisyframe.so'
    plugin.parent.mkdir(parents=True)
    plugin.write_text('fixture plugin with embedded shader\n')
    data = home / 'data'
    stamp = data / 'daisyos/kwin-frame-effect.version'
    stamp.parent.mkdir(parents=True)
    stamp.write_text(fingerprint + '\n')
    manifest = stamp.with_name(stamp.name + '.sha256')
    manifest.write_text(f'{digest(plugin)}  {plugin}\n')
    binary = home / 'bin/kwin_wayland'
    binary.parent.mkdir()
    binary.write_text('#!/bin/sh\nprintf "kwin 6.8.1\\n"\n')
    binary.chmod(0o755)
    env = dict(os.environ, HOME=temporary, XDG_DATA_HOME=str(data), PATH=str(binary.parent) + ':' + os.environ['PATH'])
    def check(expected):
        result = subprocess.run([str(installer), '--check'], env=env)
        assert (result.returncode == 0) == expected, result.returncode
    check(True)
    plugin.write_text('corrupt plugin\n')
    check(False)
    plugin.write_text('fixture plugin with embedded shader\n')
    check(True)
    stamp.write_text('old upstream effect\n')
    check(False)
    stamp.write_text(fingerprint + '\n')
    binary.write_text('#!/bin/sh\nprintf "kwin 6.8.2\\n"\n')
    check(False)
    binary.write_text('#!/bin/sh\nprintf "kwin 6.8.1\\n"\n')
    manifest.unlink()
    check(False)
    print('PASS: embedded-plugin integrity, upstream migration, KWin upgrades and missing metadata')
