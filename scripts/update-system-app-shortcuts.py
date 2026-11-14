#!/usr/bin/env python3
"""Update only known DaisyOS development binary paths in desktop copies."""
import os
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
removed_file = Path(os.environ.get('XDG_DATA_HOME', str(Path.home()/'.local/share'))) / 'daisyos/applications/removed-optional-applications'
removed = set(removed_file.read_text().lower().splitlines()) if removed_file.exists() else set()
for app in ('Files', 'Mail', 'Notes', 'Settings', 'DiskManager', 'AppInstaller', 'Calculator', 'AppStore', 'UIKit'):
    if app.lower() in removed: continue
    shortcut = desktop / f'org.daisyos.{app.lower()}.desktop'
    if not shortcut.is_file(): continue
    text = shortcut.read_text()
    for old in ('Debug', 'Release'):
        binary = repo / 'src' / f'DaisyOS.{app}' / 'bin' / old / 'net10.0' / f'DaisyOS.{app}'
        target = repo / 'src' / f'DaisyOS.{app}' / 'bin' / configuration / 'net10.0' / f'DaisyOS.{app}'
        if not target.is_file(): continue
        # Match a whole Exec value, preserving any customized command or arguments.
        suffix = ' %u' if app == 'Mail' else ' %U' if app in ('Files', 'AppInstaller') else ''
        text = text.replace(f'\nExec="{binary}"{suffix}\n', f'\nExec="{target}"{suffix}\n')
    if text != shortcut.read_text():
        shortcut.write_text(text)
        print(f'Updated {shortcut.name} to {configuration}')
