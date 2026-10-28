#!/usr/bin/env python3
"""Private-bus native portal denial/cleanup tests; no display/audio is accessed.
Run: dbus-run-session -- python3 scripts/test-continuity-media.py HELPER
"""
import pathlib
import subprocess
import sys
import time
from gi.repository import Gio, GLib

bus = Gio.bus_get_sync(Gio.BusType.SESSION, None)
name = Gio.bus_own_name_on_connection(bus, 'org.freedesktop.portal.Desktop', Gio.BusNameOwnerFlags.NONE, None, None)
interfaces = []
for iface in ('ScreenCast', 'RemoteDesktop'):
    methods = '''<method name="CreateSession"><arg type="a{sv}" direction="in"/><arg type="o" direction="out"/></method>
    <method name="Start"><arg type="o" direction="in"/><arg type="s" direction="in"/><arg type="a{sv}" direction="in"/><arg type="o" direction="out"/></method>'''
    if iface == 'ScreenCast':
        methods += '<method name="SelectSources"><arg type="o" direction="in"/><arg type="a{sv}" direction="in"/><arg type="o" direction="out"/></method>'
    else:
        methods += '<method name="SelectDevices"><arg type="o" direction="in"/><arg type="a{sv}" direction="in"/><arg type="o" direction="out"/></method>'
    interfaces.append('<interface name="org.freedesktop.portal.'+iface+'">'+methods+'</interface>')
xml = Gio.DBusNodeInfo.new_for_xml('<node>'+''.join(interfaces)+'</node>')
session_xml = Gio.DBusNodeInfo.new_for_xml('<node><interface name="org.freedesktop.portal.Session"><method name="Close"/></interface></node>')
failed_method = ''
closed = 0
calls = []
registrations = []

def method(connection, sender, path, iface, member, parameters, invocation):
    global closed
    calls.append(member)
    if member == 'Close':
        closed += 1
        invocation.return_value(GLib.Variant('()', ()))
        return
    args = parameters.unpack()
    options = args[-1]
    token = options['handle_token']
    sender_path = sender[1:].replace('.', '_')
    request = '/org/freedesktop/portal/desktop/request/'+sender_path+'/'+token
    invocation.return_value(GLib.Variant('(o)', (request,)))
    results = {}
    if member == 'CreateSession' and failed_method != member:
        session = '/org/freedesktop/portal/desktop/session/'+sender_path+'/'+options['session_handle_token']
        registrations.append(bus.register_object(session, session_xml.interfaces[0], method, None, None))
        results['session_handle'] = GLib.Variant('s', session)
    def respond():
        bus.emit_signal(sender, request, 'org.freedesktop.portal.Request', 'Response',
                        GLib.Variant('(ua{sv})', (1 if member == failed_method else 0, results)))
        return False
    GLib.idle_add(respond)

for iface in xml.interfaces:
    registrations.append(bus.register_object('/org/freedesktop/portal/desktop', iface, method, None, None))
context = GLib.MainContext.default()
helper = str(pathlib.Path(sys.argv[1]).resolve())
for mode, deny in [('--view', 'CreateSession'), ('--view', 'SelectSources'), ('--view', 'Start'),
                   ('--control', 'SelectDevices'), ('--control', 'Start')]:
    failed_method = deny
    calls.clear()
    before = closed
    with subprocess.Popen([helper, mode], stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE) as process:
        deadline = time.monotonic()+8
        while process.poll() is None and time.monotonic() < deadline:
            while context.pending(): context.iteration(False)
            time.sleep(.005)
        if process.poll() is None:
            process.kill()
            raise AssertionError('Native helper did not finish denied '+deny)
        output, error = process.communicate()
        assert process.returncode != 0, (mode, deny, error)
        assert output == b'', 'Denied portal emitted screen data'
        assert deny in calls, (deny, calls, error)
        if deny != 'CreateSession': assert closed == before+1, (deny, calls, error)
    print('PASS', mode, 'denied', deny, 'without capture or injection')
Gio.bus_unown_name(name)
