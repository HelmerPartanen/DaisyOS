"""A private real libeis server for native helper tests; never uses the compositor."""
import ctypes as C
import os

class EisFixture:
    POINTER, ABSOLUTE, KEYBOARD, SCROLL, BUTTON = 1, 2, 4, 16, 32
    def __init__(self):
        self.lib=C.CDLL('libeis.so.1')
        def bind(name,result,args):
            func=getattr(self.lib,name);func.restype=result;func.argtypes=args;return func
        P=C.c_void_p;I=C.c_int;U=C.c_uint;D=C.c_double
        for name in ['eis_new','eis_get_event','eis_event_get_client','eis_event_get_seat','eis_event_get_device','eis_seat_new_device']:
            bind(name,P,[P])
        bind('eis_client_new_seat',P,[P,C.c_char_p])
        for name in ['eis_setup_backend_fd','eis_backend_fd_add_client','eis_event_get_type','eis_client_is_sender','eis_event_keyboard_get_key','eis_event_keyboard_get_key_is_press','eis_event_button_get_button','eis_event_button_get_is_press']:
            bind(name,I,[P])
        for name in ['eis_dispatch','eis_event_unref','eis_client_connect','eis_seat_add','eis_device_add','eis_device_resume','eis_device_remove','eis_device_unref','eis_seat_unref','eis_unref','eis_region_add','eis_region_unref']:
            bind(name,None,[P])
        for name in ['eis_seat_configure_capability','eis_device_configure_capability','eis_device_configure_type','eis_device_start_emulating','eis_device_stop_emulating']:
            bind(name,None,[P,U] if name!='eis_device_stop_emulating' else [P])
        bind('eis_device_configure_size',None,[P,U,U]);bind('eis_device_configure_name',None,[P,C.c_char_p]);bind('eis_device_new_region',P,[P])
        bind('eis_device_new_keymap',P,[P,U,I,C.c_size_t]);bind('eis_keymap_add',None,[P]);bind('eis_keymap_unref',None,[P]);bind('eis_device_keyboard_send_xkb_modifiers',None,[P,U,U,U,U]);
        bind('eis_region_set_size',None,[P,U,U]);bind('eis_device_frame',None,[P,C.c_uint64])
        bind('eis_device_pointer_motion',None,[P,D,D]);bind('eis_device_keyboard_key',None,[P,U,C.c_bool])
        for name in ['eis_event_pointer_get_absolute_x','eis_event_pointer_get_absolute_y']:
            bind(name,D,[P])
        self.context=self.lib.eis_new(None);assert self.lib.eis_setup_backend_fd(self.context)==0
        self.seats=[];self.devices=[];self.events=[];self.sender=None;self.bound=False
    def client_fd(self):return self.lib.eis_backend_fd_add_client(self.context)
    def dispatch(self):
        P=C.c_void_p;U=C.c_uint
        lib=self.lib;lib.eis_dispatch(self.context)
        while True:
            event=lib.eis_get_event(self.context)
            if not event:break
            kind=lib.eis_event_get_type(event)
            if kind==1:
                client=lib.eis_event_get_client(event);self.sender=bool(lib.eis_client_is_sender(client));lib.eis_client_connect(client)
                seat=lib.eis_client_new_seat(client,b'Continuity test seat');self.seats.append(seat)
                for cap in [self.KEYBOARD,self.ABSOLUTE if self.sender else self.POINTER,self.BUTTON,self.SCROLL]:lib.eis_seat_configure_capability(seat,cap)
                lib.eis_seat_add(seat)
            elif kind==3 and not self.bound:
                self.bound=True;seat=lib.eis_event_get_seat(event)
                for name,caps in [(b'keyboard',[self.KEYBOARD]),(b'pointer',[self.ABSOLUTE if self.sender else self.POINTER,self.BUTTON,self.SCROLL])]:
                    device=lib.eis_seat_new_device(seat);lib.eis_device_configure_type(device,1 if self.sender else 2)
                    if not self.sender:lib.eis_device_configure_size(device,100,100)
                    lib.eis_device_configure_name(device,name)
                    for cap in caps:lib.eis_device_configure_capability(device,cap)
                    if self.KEYBOARD in caps:
                        xkb=C.CDLL('libxkbcommon.so.0')
                        xkb.xkb_context_new.restype=P;xkb.xkb_context_new.argtypes=[U]
                        xkb.xkb_keymap_new_from_names.restype=P;xkb.xkb_keymap_new_from_names.argtypes=[P,P,U]
                        xkb.xkb_keymap_get_as_string.restype=P;xkb.xkb_keymap_get_as_string.argtypes=[P,U]
                        xkb.xkb_keymap_mod_get_index.restype=U;xkb.xkb_keymap_mod_get_index.argtypes=[P,C.c_char_p]
                        xkb.xkb_keymap_unref.argtypes=[P];xkb.xkb_context_unref.argtypes=[P]
                        ctx=xkb.xkb_context_new(0);keymap=xkb.xkb_keymap_new_from_names(ctx,None,0)
                        text=xkb.xkb_keymap_get_as_string(keymap,1);payload=C.string_at(text)+b'\0'
                        self.ctrl_mask=1<<xkb.xkb_keymap_mod_get_index(keymap,b'Control')
                        libc=C.CDLL(None);libc.free.argtypes=[P];libc.free(text)
                        fd=os.memfd_create('continuity-fixture-keymap');os.write(fd,payload)
                        mapping=lib.eis_device_new_keymap(device,1,fd,len(payload));os.close(fd)
                        lib.eis_keymap_add(mapping);lib.eis_keymap_unref(mapping)
                        xkb.xkb_keymap_unref(keymap);xkb.xkb_context_unref(ctx)
                    if self.sender and self.ABSOLUTE in caps:
                        region=lib.eis_device_new_region(device);lib.eis_region_set_size(region,1920,1080);lib.eis_region_add(region);lib.eis_region_unref(region)
                    lib.eis_device_add(device);lib.eis_device_resume(device);self.devices.append((name,device))
                    if not self.sender and name==b'keyboard':lib.eis_device_keyboard_send_xkb_modifiers(device,self.ctrl_mask,0,0,0)
            elif kind==700:self.events.append(('key',lib.eis_event_keyboard_get_key(event),lib.eis_event_keyboard_get_key_is_press(event)))
            elif kind==500:self.events.append(('button',lib.eis_event_button_get_button(event),lib.eis_event_button_get_is_press(event)))
            elif kind==400:self.events.append(('position',lib.eis_event_pointer_get_absolute_x(event),lib.eis_event_pointer_get_absolute_y(event)))
            elif kind==201:self.events.append(('stop',))
            lib.eis_event_unref(event)
    def emit(self):
        for name,device in self.devices:
            self.lib.eis_device_start_emulating(device,1)
            if name==b'keyboard':
                self.lib.eis_device_keyboard_key(device,30,True);self.lib.eis_device_frame(device,1000)
                self.lib.eis_device_keyboard_key(device,30,False);self.lib.eis_device_frame(device,2000)
                self.lib.eis_device_keyboard_send_xkb_modifiers(device,0,0,0,0)
            else:self.lib.eis_device_pointer_motion(device,12.5,-4.5);self.lib.eis_device_frame(device,3000)
    def close(self):
        for _,device in self.devices:self.lib.eis_device_remove(device);self.lib.eis_device_unref(device)
        for seat in self.seats:self.lib.eis_seat_unref(seat)
        self.lib.eis_unref(self.context)
