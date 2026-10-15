#!/usr/bin/env python3
"""Install signed Arch MangoHud runtime archives for daisyos-game without root."""
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--package', nargs=2, action='append', metavar=('ARCHIVE', 'SIGNATURE'), required=True)
    args = parser.parse_args()
    data = Path(os.environ.get('XDG_DATA_HOME', str(Path.home() / '.local/share')))
    if not data.is_absolute():
        parser.error('XDG_DATA_HOME must be absolute.')
    target = data / 'DaisyOS/game-telemetry'
    if target.exists():
        parser.error(f'{target} already exists; leave running games unchanged and remove it before reinstalling.')
    target.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix='.telemetry-', dir=target.parent) as temporary:
        staging = Path(temporary)
        keyring = staging / 'archlinux.gpg'
        subprocess.run(['gpg', '--batch', '--dearmor', '--output', str(keyring), '/usr/share/pacman/keyrings/archlinux.gpg'], check=True)
        output = staging / 'runtime'
        manifests = output / 'manifests'
        manifests.mkdir(parents=True)
        for index, (archive, signature) in enumerate(args.package):
            subprocess.run(['gpgv', '--keyring', str(keyring), signature, archive], check=True)
            extracted = staging / f'package-{index}'
            extracted.mkdir()
            subprocess.run(['bsdtar', '-xf', archive, '-C', str(extracted), '--no-same-owner', '--no-same-permissions'], check=True)
            metadata = (extracted / '.PKGINFO').read_text()
            if not any(f'pkgname = {name}\n' in metadata for name in ('mangohud', 'lib32-mangohud', 'lib32-libxkbcommon')):
                raise ValueError('Only official MangoHud and its lib32-libxkbcommon dependency are accepted.')
            for directory in ('usr/lib/mangohud', 'usr/lib32/mangohud', 'usr/share/licenses/mangohud', 'usr/share/licenses/lib32-mangohud', 'usr/share/licenses/lib32-libxkbcommon'):
                source = extracted / directory
                if source.is_dir():
                    shutil.copytree(source, output / directory, dirs_exist_ok=True)
            for source in (extracted / 'usr/lib32').glob('libxkbcommon.so*'):
                destination = output / 'usr/lib32' / source.name
                destination.parent.mkdir(parents=True, exist_ok=True)
                shutil.copy2(source, destination)
            for source in (extracted / 'usr/share/vulkan/implicit_layer.d').glob('*.json'):
                manifest = json.loads(source.read_text())
                library = manifest['layer']['library_path']
                if not library.startswith('/usr/') or not (extracted / library.lstrip('/')).is_file():
                    raise ValueError('Unexpected MangoHud library path.')
                manifest['layer']['library_path'] = str(target / library.lstrip('/'))
                (manifests / source.name).write_text(json.dumps(manifest, indent=2) + '\n')
        if not (output / 'usr/lib/mangohud/libMangoHud.so').is_file():
            raise ValueError('The 64-bit MangoHud runtime is required.')
        (output / 'README.txt').write_text('Official Arch MangoHud runtime, verified against the installed Arch keyring. Used only by daisyos-game.\n')
        output.rename(target)
    print(f'Installed signed game telemetry runtime: {target}')


if __name__ == '__main__':
    main()
