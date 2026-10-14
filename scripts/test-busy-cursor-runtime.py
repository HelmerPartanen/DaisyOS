#!/usr/bin/env python3
"""Exercise real KWin cursor leases without opening or closing user applications."""
import argparse
import json
import time
import uuid
import dbus

parser = argparse.ArgumentParser()
parser.add_argument('--expiry', action='store_true', help='Also verify the 15-second abandoned-task watchdog')
args = parser.parse_args()

PATH = '/org/daisyos/BusyCursor1'
IFACE = 'org.daisyos.BusyCursor1'
bus = dbus.SessionBus(private=True)
other = dbus.SessionBus(private=True)
api = dbus.Interface(bus.get_object('org.kde.KWin', PATH), IFACE)
second = dbus.Interface(other.get_object('org.kde.KWin', PATH), IFACE)
def state():
    return json.loads(str(api.State()))
def wait_for(predicate, timeout=1):
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        value = state()
        if predicate(value): return value
        time.sleep(.025)
    raise AssertionError(state())
ids = [uuid.uuid4().hex for _ in range(4)]
try:
    assert state()['pending'] == 0, 'Run after user launches have finished.'
    assert not api.Begin('bad-id', False)
    assert api.Begin(ids[0], False)
    assert not state()['visible'], '150ms presentation delay was skipped'
    first = wait_for(lambda s: s['visible'])
    assert first['shape'] == 'progress', first
    assert first['width'] > 0 and first['frame'], first
    frames = set()
    for _ in range(12):
        frames.add(state()['frame']); time.sleep(.045)
    assert len(frames) > 1, 'Native working cursor is not animated'
    assert api.Begin(ids[1], True)
    wait_for(lambda s: s['visible'] and s['shape'] == 'wait')
    api.End(ids[0])
    assert state()['visible'], 'Overlapping lease cleared the remaining task'
    second.End(ids[1])
    assert state()['pending'] == 1, 'A different caller cleared another caller\'s lease'
    api.End(ids[1])
    wait_for(lambda s: not s['visible'] and s['pending'] == 0)
    assert second.Begin(ids[2], False)
    wait_for(lambda s: s['visible'])
    other.close()
    wait_for(lambda s: not s['visible'] and s['pending'] == 0)
    # Completing before the presentation delay must never show feedback.
    assert api.Begin(ids[3], False)
    api.End(ids[3])
    time.sleep(.2)
    assert not state()['visible']
    if args.expiry:
        assert api.Begin(ids[0], False)
        wait_for(lambda s: s['visible'])
        wait_for(lambda s: s['pending'] == 0 and not s['visible'], timeout=16)
        print('PASS: abandoned-task watchdog expires and stops animation')
    idle = state()
    assert idle['shape'] == '', 'Cursor animation was not stopped on completion'
    time.sleep(.15)
    assert state()['frame'] == idle['frame'], 'Cursor frames keep advancing when idle'
    print(f'PASS: animated native progress ({len(frames)} frames), wait cursor, delay, overlap, caller isolation, crash cleanup, restoration')
finally:
    for id in ids:
        api.End(id)
    bus.close()
    other.close()
