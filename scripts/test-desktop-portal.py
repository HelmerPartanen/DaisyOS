#!/usr/bin/env python3
"""Run under dbus-run-session on a real Wayland desktop; never grants permissions.
The isolated fixture owns the portal frontend name to test authenticated backend calls.
"""
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
backend = sys.argv[1]
process = subprocess.Popen([backend, '--desktop-services'], stdout=subprocess.DEVNULL, stderr=open('/tmp/daisyos-portal-test.log', 'w'))
name = 'org.freedesktop.impl.portal.desktop.daisyos'
root = '/org/freedesktop/portal/desktop'
loop = GLib.MainLoop()
failures = []

def wait_ready():
    for _ in range(100):
        if bus.name_has_owner(name): return
        if process.poll() is not None: raise RuntimeError('Backend exited; see /tmp/daisyos-portal-test.log')
        time.sleep(.1)
    raise RuntimeError('Backend did not register')

def call(interface, method, *args):
    return getattr(dbus.Interface(bus.get_object(name, root), 'org.freedesktop.impl.portal.' + interface), method)(*args)

def cancel_request(interface, method, args, path):
    result = []
    def done(*values): result.extend(values); loop.quit()
    def failed(error): failures.append(str(error)); loop.quit()
    target = dbus.Interface(bus.get_object(name, root), 'org.freedesktop.impl.portal.' + interface)
    getattr(target, method)(*args, reply_handler=done, error_handler=failed, timeout=20)
    def cancel():
        try:
            request = dbus.Interface(bus.get_object(name, path, introspect=False), 'org.freedesktop.impl.portal.Request')
            request.Close(reply_handler=lambda: None, error_handler=failed)
        except Exception as error: failed(error)
        return False
    GLib.timeout_add(900, cancel)
    timeout = GLib.timeout_add_seconds(15, lambda: (failures.append('request timed out'), loop.quit(), False)[-1])
    loop.run()
    GLib.source_remove(timeout)
    assert result and int(result[0]) == 1, (interface, result, failures)
    print('PASS', interface, method, 'cancelled with response 1')

try:
    wait_ready()
    properties = dbus.Interface(bus.get_object(name, root), 'org.freedesktop.DBus.Properties')
    assert int(properties.Get('org.freedesktop.impl.portal.ScreenCast', 'AvailableSourceTypes')) == 3
    other = dbus.bus.BusConnection(os.environ['DBUS_SESSION_BUS_ADDRESS'])
    try:
        dbus.Interface(other.get_object(name, root), 'org.freedesktop.impl.portal.FileChooser').OpenFile(
            dbus.ObjectPath(root + '/request/untrusted'), 'spoofed.app', '', 'Spoofed request', dbus.Dictionary({}, signature='sv'))
        raise AssertionError('Untrusted request was accepted')
    except dbus.exceptions.DBusException as error:
        assert error.get_dbus_name() == 'org.freedesktop.DBus.Error.AccessDenied', error
    print('PASS rejected direct application request')
    settings = dbus.Interface(other.get_object(name, root), 'org.freedesktop.impl.portal.Settings')
    appearance = settings.ReadAll(dbus.Array(['org.freedesktop.appearance'], signature='s'))
    assert int(appearance['org.freedesktop.appearance']['color-scheme']) in (1, 2)
    print('PASS appearance is readable during startup without UI-thread or frontend-owner errors')
    for index, (interface, method, tail) in enumerate([
        ('Access', 'AccessDialog', ['', 'Camera permission', 'Test app wants camera access', 'No access is granted by this test.']),
        ('FileChooser', 'OpenFile', ['', 'Choose a file']),
        ('FileChooser', 'SaveFile', ['', 'Save a file']),
        ('FileChooser', 'OpenFile', ['', 'Choose a folder']),
        ('Account', 'GetUserInformation', ['']),
        ('Screenshot', 'Screenshot', ['']),
    ]):
        path = root + '/request/test_' + str(index)
        options = dbus.Dictionary({}, signature='sv')
        if index == 3: options['directory'] = dbus.Boolean(True)
        args = [dbus.ObjectPath(path), 'org.daisyos.PortalTest', *tail, options]
        cancel_request(interface, method, args, path)
    empty = dbus.Dictionary({}, signature='sv')
    path = root + '/request/print'
    cancel_request('Print', 'PreparePrint', [dbus.ObjectPath(path), 'org.daisyos.PortalTest', '', 'Print test', empty, empty, empty], path)
    path = root + '/request/input'
    cancel_request('InputCapture', 'CreateSession', [dbus.ObjectPath(path), dbus.ObjectPath(root + '/session/input'), 'org.daisyos.PortalTest', '', dbus.Dictionary({'capabilities': dbus.UInt32(3)}, signature='sv')], path)
    path = root + '/request/usb'
    devices = dbus.Array([dbus.Struct(('fixture-device', empty, dbus.Dictionary({'writable': False}, signature='sv')), signature='sa{sv}a{sv}')], signature='(sa{sv}a{sv})')
    cancel_request('Usb', 'AcquireDevices', [dbus.ObjectPath(path), '', 'org.daisyos.PortalTest', devices, empty], path)
    shortcut_session = dbus.ObjectPath(root + '/session/shortcuts')
    assert int(call('GlobalShortcuts', 'CreateSession', dbus.ObjectPath(root + '/request/shortcuts_create'), shortcut_session, 'org.daisyos.PortalTest', empty)[0]) == 0
    path = root + '/request/shortcuts'
    shortcuts = dbus.Array([dbus.Struct(('fixture', dbus.Dictionary({'description': 'Test shortcut'}, signature='sv')), signature='sa{sv}')], signature='(sa{sv})')
    cancel_request('GlobalShortcuts', 'BindShortcuts', [dbus.ObjectPath(path), shortcut_session, shortcuts, '', empty], path)
    dbus.Interface(bus.get_object(name, shortcut_session, introspect=False), 'org.freedesktop.impl.portal.Session').Close()
    session = dbus.ObjectPath(root + '/session/test')
    options = dbus.Dictionary({}, signature='sv')
    assert int(call('RemoteDesktop', 'CreateSession', dbus.ObjectPath(root + '/request/create'), session, 'org.daisyos.PortalTest', options)[0]) == 0
    assert int(call('RemoteDesktop', 'SelectDevices', dbus.ObjectPath(root + '/request/devices'), session, 'org.daisyos.PortalTest', dbus.Dictionary({'types': dbus.UInt32(3)}, signature='sv'))[0]) == 0
    try:
        call('RemoteDesktop', 'ConnectToEIS', session, 'org.daisyos.PortalTest', options)
        raise AssertionError('Input authorized before consent')
    except dbus.exceptions.DBusException as error:
        assert error.get_dbus_name() == 'org.freedesktop.DBus.Error.AccessDenied'
    print('PASS remote input denied before consent')
    path = root + '/request/remote'
    cancel_request('RemoteDesktop', 'Start', [dbus.ObjectPath(path), session, 'org.daisyos.PortalTest', '', options], path)
    dbus.Interface(bus.get_object(name, session, introspect=False), 'org.freedesktop.impl.portal.Session').Close()
    assert not failures, failures
finally:
    process.terminate()
    try: process.wait(timeout=5)
    except subprocess.TimeoutExpired: process.kill(); process.wait()
