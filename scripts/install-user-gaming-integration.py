#!/usr/bin/env python3
"""Preserve native Steam/Lutris desktop entries while adding DaisyOS telemetry."""
import argparse
import os
from pathlib import Path
import shutil
import tempfile


def quote_exec(path):
    # Desktop Entry Exec quoting has its own escaping rules (not shell quoting).
    value = str(path).replace('\\', '\\\\').replace('"', '\\"').replace('`', '\\`').replace('$', '\\$')
    return '"' + value.replace('\\', '\\\\').replace('%', '%%') + '"'


def instrument(text, launcher):
    result = []
    section = None
    dbus_written = False
    for line in text.splitlines():
        if line.startswith('['):
            if section == '[Desktop Entry]' and not dbus_written:
                result.append('DBusActivatable=false')
            section = line
        if line.startswith('Exec='):
            command = line[5:]
            if not command.startswith((quote_exec(launcher) + ' ', str(launcher) + ' ')):
                line = 'Exec=' + quote_exec(launcher) + ' ' + command
        if section == '[Desktop Entry]' and line.startswith('DBusActivatable='):
            line = 'DBusActivatable=false'
            dbus_written = True
        result.append(line)
    if section == '[Desktop Entry]' and not dbus_written:
        result.append('DBusActivatable=false')
    return '\n'.join(result) + '\n'


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--bin-dir', type=Path, default=Path.home() / '.local/bin')
    parser.add_argument('--applications-dir', type=Path,
                        default=Path(os.environ.get('XDG_DATA_HOME', Path.home() / '.local/share')) / 'applications')
    args = parser.parse_args()
    repo_scripts = Path(__file__).resolve().parent
    args.bin_dir.mkdir(parents=True, exist_ok=True)
    for name in ('daisyos-game', 'daisyos-game-launcher', 'daisyos-gaming-environment'):
        target = args.bin_dir / name
        shutil.copyfile(repo_scripts / name, target)
        target.chmod(0o644 if name.endswith('environment') else 0o755)
    args.applications_dir.mkdir(parents=True, exist_ok=True)
    roots = [args.applications_dir] + [Path(root) / 'applications' for root in
             os.environ.get('XDG_DATA_DIRS', '/usr/local/share:/usr/share').split(':') if root]
    for name in ('steam.desktop', 'net.lutris.Lutris.desktop', 'lutris.desktop'):
        source = next((root / name for root in roots if (root / name).is_file()), None)
        if source is None:
            continue
        target = args.applications_dir / name
        text = source.read_text()
        updated = instrument(text, args.bin_dir / 'daisyos-game-launcher')
        if target.exists() and text == updated:
            continue
        if target.exists():
            backup = target.with_name(target.name + '.before-daisyos-telemetry')
            if not backup.exists():
                shutil.copy2(target, backup)
        with tempfile.NamedTemporaryFile(mode='w', dir=target.parent, delete=False) as output:
            temporary = Path(output.name)
            output.write(updated)
        try:
            temporary.chmod(0o644)
            temporary.replace(target)
        finally:
            temporary.unlink(missing_ok=True)
        print(f'Enabled game telemetry for {name}')


if __name__ == '__main__':
    main()
