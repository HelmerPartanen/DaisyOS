#!/usr/bin/env python3
"""Exercise the native launcher on a private bus and disposable XDG catalog."""
import json
import os
from pathlib import Path
import subprocess
import tempfile
import time
import uuid
import sys

ROOT = Path(__file__).resolve().parents[1]
if '--inside' not in sys.argv:
    with tempfile.TemporaryDirectory(prefix='daisy-launch-test-') as temp:
        env = os.environ.copy()
        env.update(XDG_DATA_HOME=temp+'/data', XDG_CACHE_HOME=temp+'/cache', XDG_CONFIG_HOME=temp+'/config', QT_QPA_PLATFORM='offscreen')
        raise SystemExit(subprocess.run(['dbus-run-session','--',sys.executable,__file__,'--inside',temp],env=env).returncode)

import dbus
import dbus.service
from dbus.mainloop.glib import DBusGMainLoop
from gi.repository import GLib
import threading
DBusGMainLoop(set_as_default=True)
root=Path(sys.argv[-1]); apps=root/'data/applications'; apps.mkdir(parents=True)
marker=root/'action-result'; pidfile=root/'pid'
fixture=root/'fixture with spaces.py'
fixture.write_text('#!/usr/bin/env python3\nimport os,time,pathlib,sys\npathlib.Path(sys.argv[1]).write_text(str(os.getpid()))\ntime.sleep(3)\n')
fixture.chmod(0o755)
(apps/'org.daisyos.launchtest.desktop').write_text(f'''[Desktop Entry]
Type=Application
Name=Launch test
Name[fi]=Käynnistystesti
Exec="{fixture}" {pidfile}
Actions=Test;
[Desktop Action Test]
Name=Test action
Exec="{fixture}" {marker}
''')
(apps/'org.daisyos.activatefixture.desktop').write_text("""[Desktop Entry]
Type=Application
Name=D-Bus fixture
DBusActivatable=true
Exec=/bin/false
Actions=Ping;Hang;
[Desktop Action Ping]
Name=Ping
Exec=/bin/false
[Desktop Action Hang]
Name=Hang
Exec=/bin/false
""")
subprocess.run(['kbuildsycoca6','--noincremental'],check=True,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
process=subprocess.Popen([str(ROOT/'services/DaisyLaunch/build/daisy-launch')],stdout=None,stderr=None)
bus=dbus.SessionBus()
events=[]
class ActivationFixture(dbus.service.Object):
    @dbus.service.method('org.freedesktop.Application', in_signature='a{sv}', out_signature='')
    def Activate(self, data): events.append(('activate',dict(data)))
    @dbus.service.method('org.freedesktop.Application', in_signature='asa{sv}', out_signature='')
    def Open(self, uris, data): events.append(('open',list(uris)))
    @dbus.service.method('org.freedesktop.Application', in_signature='sava{sv}', out_signature='', async_callbacks=('reply','error'))
    def ActivateAction(self, action, parameters, data, reply, error):
        events.append(('action',str(action)))
        if action != 'Hang': reply()
server_bus=dbus.bus.BusConnection(os.environ['DBUS_SESSION_BUS_ADDRESS'])
name=dbus.service.BusName('org.daisyos.activatefixture',server_bus)
receiver=ActivationFixture(name,'/org/daisyos/activatefixture')
loop=GLib.MainLoop(); threading.Thread(target=loop.run,daemon=True).start()
try:
    for _ in range(200):
        if bus.name_has_owner('org.daisyos.Launcher1'): break
        time.sleep(.02)
    api=dbus.Interface(bus.get_object('org.daisyos.Launcher1','/org/daisyos/Launcher1'),'org.daisyos.Launcher1')
    fixture_entry=next(e for e in json.loads(api.Catalog()) if e['id']=='org.daisyos.launchtest.desktop')
    assert fixture_entry['argv'][0]==str(fixture)
    assert fixture_entry['argv'][1]==str(pidfile)
    assert fixture_entry['labels']['Name']=='Launch test'
    assert fixture_entry['labels']['Name[fi]']=='Käynnistystesti' 
    def launch(app='org.daisyos.launchtest.desktop',action='',uris=(),token=''):
        id=uuid.uuid4().hex
        assert api.Launch(id,app,action,dbus.Array(uris,signature='s'),token,dbus.Dictionary({},signature='sv'))
        return id
    file_request=launch(app=str(apps/'org.daisyos.launchtest.desktop'))
    assert api.Await(file_request), 'Explicit desktop file did not use the KIO service'
    id=launch(token='private-token-do-not-log')
    assert api.Await(id)
    assert api.Launch(id,'org.daisyos.launchtest.desktop','',dbus.Array([],signature='s'),'private-token-do-not-log',dbus.Dictionary({},signature='sv'))
    trace=json.loads(api.Trace(id))
    if os.environ.get('DAISYOS_LAUNCH_TELEMETRY')=='0': assert trace=={}
    else: assert 'dispatch_accepted' in trace and 'daemon_received' in trace
    assert 'private-token' not in json.dumps(trace) and str(root) not in json.dumps(trace)
    bad=launch('org.daisyos.missing.desktop')
    try: api.Await(bad); raise AssertionError('Missing entry accepted')
    except dbus.DBusException as e: assert e.get_dbus_name().endswith('.NotFound')
    action=launch(action='Test'); 
    assert api.Await(action)
    for _ in range(100):
        if marker.exists() and pidfile.exists(): break
        time.sleep(.02)
    assert marker.exists()
    invalid=launch(action='Missing')
    try: api.Await(invalid); raise AssertionError('Missing action accepted')
    except dbus.DBusException as e: assert e.get_dbus_name().endswith('.InvalidArgument')
    try: launch(uris=['not-a-uri']); raise AssertionError('Bad URI accepted')
    except dbus.DBusException as e: assert e.get_dbus_name().endswith('.InvalidArgument')
    activate=launch('org.daisyos.activatefixture.desktop',token='private-token-do-not-log'); assert api.Await(activate)
    assert events[-1]==('activate',{'activation-token':'private-token-do-not-log'})
    opened=launch('org.daisyos.activatefixture.desktop',uris=['file:///tmp/fixture%20with%20spaces.txt']); assert api.Await(opened)
    assert events[-1]==('open',['file:///tmp/fixture%20with%20spaces.txt'])
    acted=launch('org.daisyos.activatefixture.desktop',action='Ping'); assert api.Await(acted)
    assert events[-1]==('action','Ping')
    hang=launch('org.daisyos.activatefixture.desktop',action='Hang'); api.Cancel(hang)
    try: api.Await(hang); raise AssertionError('Cancelled request accepted')
    except dbus.DBusException as e: assert e.get_dbus_name().endswith('.Cancelled')
    second=dbus.bus.BusConnection(os.environ['DBUS_SESSION_BUS_ADDRESS'])
    other=dbus.Interface(second.get_object('org.daisyos.Launcher1','/org/daisyos/Launcher1'),'org.daisyos.Launcher1')
    try: other.Await(id); raise AssertionError('Other caller accessed request')
    except dbus.DBusException as e: assert e.get_dbus_name().endswith('.NotFound')
    (apps/'org.daisyos.addedfixture.desktop').write_text('[Desktop Entry]\nType=Application\nName=Added fixture\nExec=/bin/true\n')
    for _ in range(250):
        if any(e['id']=='org.daisyos.addedfixture.desktop' for e in json.loads(api.Catalog())): break
        time.sleep(.02)
    else: raise AssertionError('Registry did not refresh')
    # Relaunch a sleeping fixture immediately before testing process survival.
    time.sleep(.15)
    survival=launch(); assert api.Await(survival)
    time.sleep(.1)
    process.terminate(); process.wait(timeout=5)
    os.kill(int(pidfile.read_text()),0) # launched application survives the service
    print('PASS: registry, KIO dispatch, actions, idempotence, missing entries, URI validation, private traces, app survival, D-Bus Activate/Open/actions, cancellation, owner isolation, live registry refresh')
finally:
    if process.poll() is None: process.terminate(); process.wait(timeout=5)
    loop.quit()
    time.sleep(3) # fixture children exit before disposable profile cleanup
