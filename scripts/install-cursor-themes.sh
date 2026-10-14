#!/usr/bin/env bash
set -Eeuo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
python3 "$repo_root/scripts/build-cursor-themes.py"
icon_root="${XDG_DATA_HOME:-$HOME/.local/share}/icons"
mkdir -p "$icon_root"
python3 - "$repo_root/artifacts/cursors" "$icon_root" <<'PY_SYNC'
from pathlib import Path
import shutil
import sys
source, destination = map(Path, sys.argv[1:])
for name in ('DaisyOS-Dark', 'DaisyOS-Light', 'DaisyOS-Dark-Small', 'DaisyOS-Light-Small'):
    src = source / name; dst = destination / name
    (dst / 'cursors').mkdir(parents=True, exist_ok=True)
    for file in (src / 'cursors').iterdir():
        temp = dst / 'cursors' / (file.name + '.new')
        temp.unlink(missing_ok=True)
        if file.is_symlink(): temp.symlink_to(file.readlink())
        else: shutil.copy2(file, temp)
        temp.replace(dst / 'cursors' / file.name)
    expected = {file.name for file in (src / 'cursors').iterdir()}
    for file in (dst / 'cursors').iterdir():
        if file.name not in expected: file.unlink()
    temp = dst / 'index.theme.new'
    shutil.copy2(src / 'index.theme', temp)
    temp.replace(dst / 'index.theme')
PY_SYNC

echo "Installed DaisyOS native cursor themes. Select them in Settings > Input."
