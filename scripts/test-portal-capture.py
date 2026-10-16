#!/usr/bin/env python3
"""Read one real compositor frame without recording or sending it anywhere.
Requires a running Wayland session, registered helper desktop entry, pw-dump and
GStreamer's pipewiresrc/videoconvert/fakesink plugins.
"""
import json
import pathlib
import select
import subprocess
import sys
import time

helper = pathlib.Path(__file__).resolve().parents[1] / 'src/DaisyOS.Shell/bin/Debug/net10.0/daisyos-portal-capture'
if len(sys.argv) != 2:
    raise SystemExit('Usage: test-portal-capture.py OUTPUT_NAME')
process = subprocess.Popen([str(helper), '--output', sys.argv[1], '2'], stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
serial = None
try:
    assert select.select([process.stdout], [], [], 10)[0], 'No capture node received'
    line = process.stdout.readline().strip()
    assert line.startswith('node '), line
    node = int(line.split()[1])
    objects = json.loads(subprocess.check_output(['pw-dump']))
    serial = next(item['info']['props']['object.serial'] for item in objects if item['id'] == node)
    result = subprocess.run(['gst-launch-1.0', '-q', 'pipewiresrc', 'target-object=' + str(serial), 'num-buffers=1', 'always-copy=true', '!', 'video/x-raw,format=BGRx', '!', 'videoconvert', '!', 'fakesink', 'sync=false'], capture_output=True, text=True, timeout=15)
    assert result.returncode == 0, result.stderr
    print('PASS received a real compositor video frame through PipeWire')
finally:
    process.stdin.close()
    try:
        process.wait(timeout=5)
    except subprocess.TimeoutExpired:
        process.kill()
        process.wait()
if serial is not None:
    for _ in range(30):
        objects = json.loads(subprocess.check_output(['pw-dump']))
        if not any(item.get('info', {}).get('props', {}).get('object.serial') == serial for item in objects):
            print('PASS compositor capture node removed after the helper closed')
            break
        time.sleep(.1)
    else:
        raise AssertionError('Capture node remained after its session closed')
