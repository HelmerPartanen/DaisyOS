#!/usr/bin/env python3
"""Update only known DaisyOS development binary paths in desktop copies."""
import shutil
import subprocess
import sys
from pathlib import Path

repo = Path(sys.argv[1]).resolve()
configuration = sys.argv[2]
if configuration not in ('Debug', 'Release'):
    raise SystemExit('Invalid app configuration')
desktop = Path.home() / 'Desktop'
if shutil.which('xdg-user-dir'):
    result = subprocess.run(['xdg-user-dir', 'DESKTOP'], capture_output=True, text=True, timeout=2)
    if result.returncode == 0 and result.stdout.strip(): desktop = Path(result.stdout.strip())
for app in ('Files', 'Notes', 'Settings', 'DiskManager'):
    shortcut = desktop / f'org.daisyos.{app.lower()}.desktop'
    if not shortcut.is_file(): continue
    text = shortcut.read_text()
    for old in ('Debug', 'Release'):
        binary = repo / 'src' / f'DaisyOS.{app}' / 'bin' / old / 'net10.0' / f'DaisyOS.{app}'
        target = repo / 'src' / f'DaisyOS.{app}' / 'bin' / configuration / 'net10.0' / f'DaisyOS.{app}'
        if not target.is_file(): continue
        # Match a whole Exec value, preserving any customized command or arguments.
        suffix = ' %U' if app == 'Files' else ''
        text = text.replace(f'\nExec="{binary}"{suffix}\n', f'\nExec="{target}"{suffix}\n')
    if text != shortcut.read_text():
        shortcut.write_text(text)
        print(f'Updated {shortcut.name} to {configuration}')
