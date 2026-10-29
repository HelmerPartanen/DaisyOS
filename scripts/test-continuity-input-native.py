#!/usr/bin/env python3
"""Real libei injection through a private portal/libeis fixture, no global input."""
import os,pathlib,struct,subprocess,sys,time
from gi.repository import Gio,GLib
from continuity_eis_fixture import EisFixture
bus=Gio.bus_get_sync(Gio.BusType.SESSION,None)
owner=Gio.bus_own_name_on_connection(bus,'org.freedesktop.portal.Desktop',Gio.BusNameOwnerFlags.NONE,None,None)
xml=Gio.DBusNodeInfo.new_for_xml('''<node><interface name="org.freedesktop.portal.RemoteDesktop">
<method name="CreateSession"><arg type="a{sv}" direction="in"/><arg type="o" direction="out"/></method>
<method name="SelectDevices"><arg type="o" direction="in"/><arg type="a{sv}" direction="in"/><arg type="o" direction="out"/></method>
<method name="Start"><arg type="o" direction="in"/><arg type="s" direction="in"/><arg type="a{sv}" direction="in"/><arg type="o" direction="out"/></method>
<method name="ConnectToEIS"><arg type="o" direction="in"/><arg type="a{sv}" direction="in"/><arg type="h" direction="out"/></method>
</interface></node>''')
close_xml=Gio.DBusNodeInfo.new_for_xml('<node><interface name="org.freedesktop.portal.Session"><method name="Close"/></interface></node>')
server=EisFixture();registrations=[];closed=[]
def method(connection,sender,path,iface,member,parameters,invocation):
    args=parameters.unpack()
    if member=='Close':closed.append(path);invocation.return_value(GLib.Variant('()',()));return
    if member=='ConnectToEIS':
        fd=server.client_fd();fds=Gio.UnixFDList.new();index=fds.append(fd);os.close(fd)
        invocation.return_value_with_unix_fd_list(GLib.Variant('(h)',(index,)),fds);return
    options=args[-1];sender_path=sender[1:].replace('.','_');token=options['handle_token']
    request='/org/freedesktop/portal/desktop/request/'+sender_path+'/'+token
    results={}
    if member=='CreateSession':
        session='/org/freedesktop/portal/desktop/session/'+sender_path+'/'+options['session_handle_token']
        results={'session_handle':GLib.Variant('s',session)}
        registrations.append(bus.register_object(session,close_xml.interfaces[0],method,None,None))
    if member=='Start':results={'devices':GLib.Variant('u',3)}
    invocation.return_value(GLib.Variant('(o)',(request,)))
    def respond():bus.emit_signal(sender,request,'org.freedesktop.portal.Request','Response',GLib.Variant('(ua{sv})',(0,results)));return False
    GLib.idle_add(respond)
registrations.append(bus.register_object('/org/freedesktop/portal/desktop',xml.interfaces[0],method,None,None))
context=GLib.MainContext.default();helper=str(pathlib.Path(sys.argv[1]).resolve())
with subprocess.Popen([helper,'--input'],stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.PIPE) as process:
    try:
        os.set_blocking(process.stdout.fileno(),False);buffer=b'';sent=False;returned=False;deadline=time.monotonic()+10
        while time.monotonic()<deadline and process.poll() is None:
            while context.pending():context.iteration(False)
            server.dispatch()
            try:buffer+=os.read(process.stdout.fileno(),4096)
            except BlockingIOError:pass
            while len(buffer)>=4:
                size=struct.unpack('>I',buffer[:4])[0]
                if len(buffer)<4+size:break
                data,buffer=buffer[4:4+size],buffer[4+size:]
                if not sent:
                    assert size==0
                    process.stdin.write(b'6 1 0 0.5 0\n1 30 1 0 0\n1 30 0 0 0\n2 272 1 0 0\n2 272 0 0 0\n3 0 0 80 0\n3 0 0 -200 0\n')
                    process.stdin.flush();sent=True
                elif data:
                    values=data.decode().split();assert values[:3]==['7','0','0'],data
                    assert abs(float(values[3])-.5)<.001
                    returned=True
            if returned and ('key',30,1) in server.events and ('key',30,0) in server.events and not process.stdin.closed:process.stdin.close()
            time.sleep(.002)
        if process.poll() is None:
            process.kill();process.wait();raise AssertionError(('helper did not exit',server.events,process.stderr.read().decode()))
        error=process.stderr.read().decode();assert process.returncode==0,(error,server.events)
        assert returned and closed,(returned,closed,error,server.events)
        assert ('key',30,1) in server.events and ('key',30,0) in server.events,server.events
        assert ('button',272,1) in server.events and ('button',272,0) in server.events,server.events
        assert any(e[0]=='position' and e[1]>50 for e in server.events),server.events
        assert ('stop',) in server.events,server.events
        print('PASS: real libei keyboard, button, absolute pointer, return edge and emulation release through an isolated libeis server')
    finally:
        if process.poll() is None:process.kill();process.wait()
server.close();Gio.bus_unown_name(owner)
