#!/usr/bin/env python3
"""Private-bus InputCapture lifecycle test. Never connects to the real compositor."""
import pathlib, socket, struct, subprocess, sys, time, os
from gi.repository import Gio, GLib
from continuity_eis_fixture import EisFixture
bus=Gio.bus_get_sync(Gio.BusType.SESSION,None)
owner=Gio.bus_own_name_on_connection(bus,'org.freedesktop.portal.Desktop',Gio.BusNameOwnerFlags.NONE,None,None)
xml=Gio.DBusNodeInfo.new_for_xml('''<node><interface name="org.freedesktop.portal.InputCapture">
<method name="CreateSession"><arg type="s" direction="in"/><arg type="a{sv}" direction="in"/><arg type="o" direction="out"/></method>
<method name="GetZones"><arg type="o" direction="in"/><arg type="a{sv}" direction="in"/><arg type="o" direction="out"/></method>
<method name="SetPointerBarriers"><arg type="o" direction="in"/><arg type="a{sv}" direction="in"/><arg type="aa{sv}" direction="in"/><arg type="u" direction="in"/><arg type="o" direction="out"/></method>
<method name="ConnectToEIS"><arg type="o" direction="in"/><arg type="a{sv}" direction="in"/><arg type="h" direction="out"/></method>
<method name="Enable"><arg type="o" direction="in"/><arg type="a{sv}" direction="in"/></method>
<method name="Disable"><arg type="o" direction="in"/><arg type="a{sv}" direction="in"/></method>
<method name="Release"><arg type="o" direction="in"/><arg type="a{sv}" direction="in"/></method>
</interface></node>''')
close_xml=Gio.DBusNodeInfo.new_for_xml('<node><interface name="org.freedesktop.portal.Session"><method name="Close"/></interface></node>')
server=EisFixture();registrations=[];session=None;released=[];closed=[];enabled=False;calls=[]

def method(connection,sender,path,iface,member,parameters,invocation):
    global session,enabled
    calls.append(member)
    args=parameters.unpack()
    if member=='ConnectToEIS':
        fd=server.client_fd();fds=Gio.UnixFDList.new();handle=fds.append(fd);os.close(fd)
        invocation.return_value_with_unix_fd_list(GLib.Variant('(h)',(handle,)),fds);return
    if member in ['Close','Enable','Disable','Release']:
        invocation.return_value(GLib.Variant('()',()))
        if member=='Close':closed.append(path)
        if member=='Release':
            released.append(args[1])
            if len(released)==1:
                def reactivate():
                    bus.emit_signal(sender,'/org/freedesktop/portal/desktop','org.freedesktop.portal.InputCapture','Activated',GLib.Variant('(oa{sv})',(session,{
                        'activation_id':GLib.Variant('u',8),'barrier_id':GLib.Variant('u',1),'cursor_position':GLib.Variant('(dd)',(3840.,540.))})))
                    return False
                GLib.timeout_add(80,reactivate)
        if member=='Enable' and not enabled:
            enabled=True
            def activate():
                bus.emit_signal(sender,'/org/freedesktop/portal/desktop','org.freedesktop.portal.InputCapture','Activated',GLib.Variant('(oa{sv})',(session,{
                    'activation_id':GLib.Variant('u',7),'barrier_id':GLib.Variant('u',1),'cursor_position':GLib.Variant('(dd)',(3840.,540.))})))
                return False
            GLib.timeout_add(30,activate)
        return
    options=args[1];token=options['handle_token'];sender_path=sender[1:].replace('.','_')
    request='/org/freedesktop/portal/desktop/request/'+sender_path+'/'+token
    results={}
    if member=='CreateSession':
        assert options['capabilities']==3
        session='/org/freedesktop/portal/desktop/session/'+sender_path+'/'+options['session_handle_token']
        registrations.append(bus.register_object(session,close_xml.interfaces[0],method,None,None))
        results={'session_handle':GLib.Variant('o',session),'capabilities':GLib.Variant('u',3)}
    elif member=='GetZones':results={'zone_set':GLib.Variant('u',4),'zones':GLib.Variant('a(uuii)',[(1920,1080,0,0),(1920,1080,1920,0)])}
    elif member=='SetPointerBarriers':
        assert args[3]==4
        assert args[2]==[{'barrier_id':1,'position':(3840,0,3840,1079)}],args[2]
        results={'failed_barriers':GLib.Variant('au',[])}
    invocation.return_value(GLib.Variant('(o)',(request,)))
    def respond():
        bus.emit_signal(sender,request,'org.freedesktop.portal.Request','Response',GLib.Variant('(ua{sv})',(0,results)))
        return False
    GLib.idle_add(respond)

registrations.append(bus.register_object('/org/freedesktop/portal/desktop',xml.interfaces[0],method,None,None))
context=GLib.MainContext.default();helper=str(pathlib.Path(sys.argv[1]).resolve())
with subprocess.Popen([helper,'--capture','right'],stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.PIPE) as process:
    try:
        os.set_blocking(process.stdout.fileno(),False);buffer=b'';entered=False;entries=0;sent_release=False;emitted=False;keys=[];motion=False;deadline=time.monotonic()+8
        while time.monotonic()<deadline and process.poll() is None:
            while context.pending():context.iteration(False)
            server.dispatch()
            if entered and server.devices and not emitted:server.emit();emitted=True
            try:buffer+=os.read(process.stdout.fileno(),4096)
            except BlockingIOError:pass
            while len(buffer)>=4:
                size=struct.unpack('>I',buffer[:4])[0]
                if len(buffer)<4+size:break
                data,buffer=buffer[4:4+size],buffer[4+size:]
                if data:
                    values=data.decode().split()
                    if values[0]=='6':
                        assert values[:3]==['6','1','0'],data
                        assert abs(float(values[3])-.5)<.001
                        entered=True;entries+=1;sent_release=False
                    elif values[0]=='1':keys.append(values[:3])
                    elif values[0]=='3':
                        assert float(values[3])==12.5 and float(values[4])==-4.5,data
                        motion=True
            if entered and motion and ['1','30','0'] in keys and ['1','29','0'] in keys and not sent_release:
                process.stdin.write(b'release 0.25\n');process.stdin.flush();sent_release=True
            if len(released)>=2 and not process.stdin.closed:process.stdin.close()
            time.sleep(.005)
        if process.poll() is None:
            process.kill();process.wait()
            raise AssertionError(('InputCapture helper did not close safely',calls,buffer,entered,released,process.stderr.read().decode()))
        error=process.stderr.read().decode()
        assert process.returncode==0,error
        assert entered and released and closed,(entered,released,closed,error)
        assert entries==2 and len(released)==2,(entries,released)
        assert released[1]['activation_id']==8
        assert released[0]['activation_id']==7
        assert released[0]['cursor_position']==(3838.,269.75),released
        assert ['1','30','1'] in keys and ['1','30','0'] in keys,keys
        assert ['1','29','1'] in keys and ['1','29','0'] in keys,keys
        print('PASS: outside barrier, real libei captured motion/keys, two edge crossings without relinking, proportional return and clean session close')
    finally:
        if process.poll() is None:process.kill();process.wait()
server.close()
Gio.bus_unown_name(owner)
