#!/usr/bin/env python3
"""Read-only, bounded environment capture for desktop/game comparisons."""
import argparse
import datetime
import json
import os
from pathlib import Path
import platform
import subprocess


def capture(command):
    try:
        result = subprocess.run(command, text=True, capture_output=True, timeout=10)
        return {'command': command, 'exitCode': result.returncode,
                'stdout': result.stdout[:65536], 'stderr': result.stderr[:4096]}
    except (OSError, subprocess.TimeoutExpired) as error:
        return {'command': command, 'unavailable': str(error)}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('output', type=Path)
    parser.add_argument('--label', required=True)
    parser.add_argument('--workload', default='desktop')
    args = parser.parse_args()
    root = Path(__file__).resolve().parent.parent
    commands = {
        'commit': ['git', '-C', str(root), 'rev-parse', 'HEAD'],
        'workingTree': ['git', '-C', str(root), 'status', '--short'],
        'gpu': ['lspci', '-nnk'],
        'outputs': ['kscreen-doctor', '-o'],
        'compositor': ['qdbus6', 'org.kde.KWin', '/KWin', 'org.kde.KWin.supportInformation'],
        'powerProfile': ['powerprofilesctl', 'get'],
        'runtime': ['dotnet', '--info'],
    }
    report = {
        'protocol': 1, 'label': args.label, 'workload': args.workload,
        'capturedUtc': datetime.datetime.now(datetime.timezone.utc).isoformat(),
        'kernel': platform.release(),
        'session': {key: os.environ.get(key) for key in
                    ['XDG_SESSION_TYPE', 'WAYLAND_DISPLAY', 'DISPLAY']},
        'diagnostics': {name: capture(command) for name, command in commands.items()},
        'presentationMeasured': False, 'gameplayMeasured': False,
        'manualFieldsRequired': ['game/version/scene/settings', 'build configuration',
                                 'Game Mode', 'wallpaper', 'overlay',
                                 'observed direct scanout and VRR state'],
    }
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, indent=2) + '\n')


if __name__ == '__main__':
    main()
