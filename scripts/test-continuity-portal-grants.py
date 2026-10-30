#!/usr/bin/env python3
"""Run with dbus-run-session on Wayland: scoped Continuity portal grants.
Uses isolated daemon and KWin fixtures; no real screen or input access is granted.
"""
import json
import os
import subprocess
import sys
import time
import dbus
import dbus.service
import dbus.mainloop.glib
from gi.repository import GLib

dbus.mainloop.glib.DBusGMainLoop(set_as_default=True)
bus = dbus.SessionBus()
frontend = dbus.service.BusName('org.freedesktop.portal.Desktop', bus=bus)
continuity = dbus.service.BusName('org.daisyos.Continuity1', bus=bus)
kwin = dbus.service.BusName('org.kde.KWin', bus=bus)
root = '/org/freedesktop/portal/desktop'
grant = 'a' * 64
validated = []
captures = []

class Daemon(dbus.service.Object):
    @dbus.service.method('org.daisyos.Continuity1', in_signature='s', out_signature='s')
    def Execute(self, value):
        command = json.loads(value)
        assert command['action'] == 'capture-grant', command
        assert command['value'] == grant, command
        validated.append(command)
        return json.dumps(dict(enabled=True, visible=True, paused=False, sessionReady=True,
            name='Fixture', deviceId='fixture', clipboardStatus='', diagnostic='', devices=[], prompts=[], transfers=[], sessions=[]))

class CaptureManager(dbus.service.Object):
    @dbus.service.method('org.kde.KWin.EIS.InputCaptureManager', in_signature='u', out_signature='o')
    def addInputCapture(self, capabilities):
        captures.append(capabilities)
        return '/org/kde/KWin/EIS/InputCapture/fixture'
    @dbus.service.method('org.kde.KWin.EIS.InputCaptureManager', in_signature='o')
    def removeInputCapture(self, path):
        pass

daemon = Daemon(bus, '/org/daisyos/Continuity1')
manager = CaptureManager(bus, '/org/kde/KWin/EIS/InputCapture')
backend_name = 'org.freedesktop.impl.portal.desktop.daisyos'
log = open('/tmp/daisy-continuity-portal-grants.log', 'w')
process = subprocess.Popen([sys.argv[1], '--desktop-services'], stdout=subprocess.DEVNULL, stderr=log)
loop = GLib.MainLoop()

def call(portal, method, *args):
    result, errors = [], []
    def done(*values): result.extend(values); loop.quit()
    def failed(error): errors.append(str(error)); loop.quit()
    target = dbus.Interface(bus.get_object(backend_name, root), 'org.freedesktop.impl.portal.' + portal)
    getattr(target, method)(*args, reply_handler=done, error_handler=failed, timeout=5)
    timeout = GLib.timeout_add_seconds(6, lambda: (errors.append('Unexpected approval dialog or timeout'), loop.quit(), False)[-1])
    loop.run()
    GLib.source_remove(timeout)
    assert not errors, errors
    assert result and int(result[0]) == 0, result
    return result

try:
    for _ in range(100):
        if bus.name_has_owner(backend_name): break
        assert process.poll() is None, 'Backend exited; see /tmp/daisy-continuity-portal-grants.log'
        time.sleep(.1)
    else: raise AssertionError('Backend did not register')
    options = dbus.Dictionary({}, signature='sv')
    session = dbus.ObjectPath(root + '/session/fixture/continuity_' + grant)
    request = dbus.ObjectPath(root + '/request/fixture/create')
    call('InputCapture', 'CreateSession', request, session, '', '', dbus.Dictionary({'capabilities': dbus.UInt32(3)}, signature='sv'))
    assert len(captures) == 1 and len(validated) == 1
    remote = dbus.ObjectPath(root + '/session/remote/continuity_' + grant)
    call('RemoteDesktop', 'CreateSession', request, remote, '', options)
    call('RemoteDesktop', 'SelectDevices', request, remote, '', dbus.Dictionary({'types': dbus.UInt32(3)}, signature='sv'))
    call('RemoteDesktop', 'Start', request, remote, '', '', options)
    assert len(validated) == 2
    print('PASS: authenticated Continuity input capture and remote input start without approval dialogs through the real portal backend')
finally:
    process.terminate()
    try: process.wait(timeout=4)
    except subprocess.TimeoutExpired: process.kill(); process.wait()
    log.close()
