#!/usr/bin/env python3
"""Read-only DaisyOS process inventory; never exports full process arguments."""
import json
from pathlib import Path


def snapshot():
    rows = []
    for entry in Path('/proc').iterdir():
        if not entry.name.isdigit():
            continue
        try:
            name = (entry / 'comm').read_text().strip()
            if not name.lower().startswith('daisy'):
                continue
            args = (entry / 'cmdline').read_bytes().split(b'\0')
            role = 'desktop services' if b'--desktop-services' in args else name
            values = {}
            for filename in ['smaps_rollup', 'status']:
                for line in (entry / filename).read_text().splitlines():
                    key, _, value = line.partition(':')
                    if key in ['Rss', 'Pss', 'Private_Dirty', 'Private_Clean', 'VmSize', 'Threads']:
                        values[key] = int(value.split()[0])
            rows.append({'pid': int(entry.name), 'role': role, **values})
        except (OSError, ValueError):
            continue  # process exited, or a different user's /proc entry
    return {'units': 'KiB except Threads (count)', 'processes': rows}


if __name__ == '__main__':
    print(json.dumps(snapshot(), indent=2))
