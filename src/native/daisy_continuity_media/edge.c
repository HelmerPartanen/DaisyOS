// Included by main.c: shares the portal request, libei injection and framing primitives.
#include "edge_geometry.h"
#include <sys/mman.h>
#include <xkbcommon/xkbcommon.h>
#define CAPTURE "org.freedesktop.portal.InputCapture"
static gboolean edge_captured, edge_suspended, edge_rearm;
static guint edge_activation;
static unsigned edge_side;
static double zone_x, zone_y, zone_width, zone_height;
static struct { double x,y,w,h; } capture_zones[64];
static unsigned capture_zone_count;
static gboolean edge_emit(unsigned kind,unsigned code,unsigned state,double x,double y);
static gboolean capture_keys[768], synthetic_modifiers[768];
static struct xkb_context *capture_xkb;
static struct xkb_keymap *capture_map;
static struct ei_keymap *capture_ei_map;
static uint32_t capture_modifiers;
static gboolean sync_modifiers(void) {
    if(!edge_captured||!capture_map)return TRUE;
    const char *names[]={XKB_MOD_NAME_SHIFT,XKB_MOD_NAME_CTRL,XKB_MOD_NAME_ALT,XKB_MOD_NAME_LOGO,"Mod5"};
    const unsigned keys[]={42,29,56,125,100},other[]={54,97,0,126,0};
    for(unsigned i=0;i<5;i++) {
        xkb_mod_index_t index=xkb_keymap_mod_get_index(capture_map,names[i]);
        gboolean down=index!=XKB_MOD_INVALID&&index<32&&(capture_modifiers&(1u<<index));
        unsigned key=keys[i];
        if(down&&!capture_keys[key]&&(!other[i]||!capture_keys[other[i]])) {
            capture_keys[key]=TRUE;synthetic_modifiers[key]=TRUE;if(!edge_emit(1,key,1,0,0))return FALSE;
        }else if(!down&&synthetic_modifiers[key]){
            synthetic_modifiers[key]=FALSE;
            if(capture_keys[key]){capture_keys[key]=FALSE;if(!edge_emit(1,key,0,0,0))return FALSE;}
        }
    }
    return TRUE;
}
static void capture_modifier_event(struct ei_event *event) {
    struct ei_device *device=ei_event_get_device(event);
    struct ei_keymap *keymap=device?ei_device_keyboard_get_keymap(device):NULL;
    if(keymap&&keymap!=capture_ei_map&&ei_keymap_get_type(keymap)==EI_KEYMAP_TYPE_XKB) {
        size_t size=ei_keymap_get_size(keymap);
        if(size>0&&size<=2*1024*1024) {
            void *bytes=mmap(NULL,size,PROT_READ,MAP_PRIVATE,ei_keymap_get_fd(keymap),0);
            if(bytes!=MAP_FAILED) {
                if(!capture_xkb)capture_xkb=xkb_context_new(XKB_CONTEXT_NO_FLAGS);
                struct xkb_keymap *map=capture_xkb?xkb_keymap_new_from_buffer(capture_xkb,bytes,size-(((char*)bytes)[size-1]==0),XKB_KEYMAP_FORMAT_TEXT_V1,XKB_KEYMAP_COMPILE_NO_FLAGS):NULL;
                munmap(bytes,size);
                if(map){if(capture_map)xkb_keymap_unref(capture_map);if(capture_ei_map)ei_keymap_unref(capture_ei_map);capture_map=map;capture_ei_map=ei_keymap_ref(keymap);}
            }
        }
    }
    capture_modifiers=ei_event_keyboard_get_xkb_mods_depressed(event);
    if(!sync_modifiers())running=FALSE;
}
static struct edge_position remote_position;
static double remote_origin_x, remote_origin_y;
static gboolean edge_emit(unsigned kind,unsigned code,unsigned state,double x,double y) {
    char line[128];int n=snprintf(line,sizeof line,"%u %u %u %.6f %.6f",kind,code,state,x,y);
    return n>0 && (size_t)n<sizeof line && write_frame(line,(size_t)n);
}
static void stop_emulation(void) {
    for(int i=0;i<16;i++)if(devices[i]&&resumed[i]) {
        // End the emulation sequence to release all held keys/buttons in KWin.
        ei_device_stop_emulating(devices[i]);ei_device_start_emulating(devices[i],serial++);
        memset(held_keys[i],0,sizeof held_keys[i]);memset(held_buttons[i],0,sizeof held_buttons[i]);
    }
}
static void release_capture(double fraction) {
    if(!edge_captured)return;
    GVariantBuilder options;g_variant_builder_init(&options,G_VARIANT_TYPE_VARDICT);
    g_variant_builder_add(&options,"{sv}","activation_id",g_variant_new_uint32(edge_activation));
    double x=edge_side==0?zone_x+1:edge_side==1?zone_x+zone_width-2:zone_x+edge_clamp(fraction,0,1)*(zone_width-1);
    double y=edge_side==2?zone_y+1:edge_side==3?zone_y+zone_height-2:zone_y+edge_clamp(fraction,0,1)*(zone_height-1);
    g_variant_builder_add(&options,"{sv}","cursor_position",g_variant_new("(dd)",x,y));
    GVariant *reply=call(CAPTURE,"Release",g_variant_new("(oa{sv})",session,&options));
    if(reply)g_variant_unref(reply);else running=FALSE;
    edge_captured=FALSE;memset(capture_keys,0,sizeof capture_keys);
}
static void capture_signal(GDBusConnection *connection,const gchar *sender,const gchar *path,const gchar *iface,const gchar *member,GVariant *args,gpointer data) {
    (void)connection;(void)sender;(void)path;(void)iface;(void)data;
    const char *handle;GVariant *options;g_variant_get(args,"(&o@a{sv})",&handle,&options);
    if(!session||strcmp(handle,session)){g_variant_unref(options);return;}
    if(!strcmp(member,"Activated")) {
        double x=0,y=0;guint barrier=0;g_variant_lookup(options,"activation_id","u",&edge_activation);
        if(g_variant_lookup(options,"barrier_id","u",&barrier)&&barrier>0&&barrier<=capture_zone_count) {
            zone_x=capture_zones[barrier-1].x;zone_y=capture_zones[barrier-1].y;zone_width=capture_zones[barrier-1].w;zone_height=capture_zones[barrier-1].h;
        }
        g_variant_lookup(options,"cursor_position","(dd)",&x,&y);
        edge_captured=TRUE;memset(capture_keys,0,sizeof capture_keys);memset(synthetic_modifiers,0,sizeof synthetic_modifiers);
        double fraction=edge_side<2?(y-zone_y)/(zone_height-1):(x-zone_x)/(zone_width-1);
        if(!edge_emit(6,edge_side,0,edge_clamp(fraction,0,1),0)||!sync_modifiers())running=FALSE;
    } else if(!strcmp(member,"Deactivated")) {
        edge_captured=FALSE;if(!edge_emit(7,0,0,0,0))running=FALSE;
    } else if(!strcmp(member,"Disabled")) {
        // A compositor may disable capture when returning the pointer. Keep the
        // authorized portal session and rearm it instead of recreating the link.
        if(!edge_suspended)edge_rearm=TRUE;
    } else if(!strcmp(member,"ZonesChanged")) {
        // Topology changes invalidate every barrier. Stop safely; reconnect rebuilds it.
        running=FALSE;
    }
    g_variant_unref(options);
}
static gboolean create_edge_session(gboolean capture) {
    char token[96];const char *grant=getenv("DAISY_CONTINUITY_GRANT");
    if(grant)snprintf(token,sizeof token,"continuity_%s",grant);else snprintf(token,sizeof token,"continuity_edge_%d",getpid());
    GVariantBuilder opts;g_variant_builder_init(&opts,G_VARIANT_TYPE_VARDICT);
    g_variant_builder_add(&opts,"{sv}","handle_token",g_variant_new_string(token));
    g_variant_builder_add(&opts,"{sv}","session_handle_token",g_variant_new_string(token));
    if(capture)g_variant_builder_add(&opts,"{sv}","capabilities",g_variant_new_uint32(3));
    GVariant *result=request(capture?CAPTURE:"org.freedesktop.portal.RemoteDesktop","CreateSession",token,
        capture?g_variant_new("(sa{sv})","",&opts):g_variant_new("(a{sv})",&opts));
    if(!result)return FALSE;
    if(!g_variant_lookup(result,"session_handle","o",&session))g_variant_lookup(result,"session_handle","s",&session);
    guint capabilities=0;
    gboolean allowed=!capture||(g_variant_lookup(result,"capabilities","u",&capabilities)&&(capabilities&3)==3);
    g_variant_unref(result);return session!=NULL&&allowed;
}
static gboolean connect_edge_eis(gboolean capture) {
    GUnixFDList *fds=NULL;GVariant *reply=g_dbus_connection_call_with_unix_fd_list_sync(bus,DESKTOP,PATH,
        capture?CAPTURE:"org.freedesktop.portal.RemoteDesktop","ConnectToEIS",g_variant_new("(o@a{sv})",session,empty()),
        G_VARIANT_TYPE("(h)"),G_DBUS_CALL_FLAGS_NONE,10000,NULL,&fds,NULL,NULL);
    if(!reply||!fds){if(reply)g_variant_unref(reply);g_clear_object(&fds);return FALSE;}
    gint handle;g_variant_get(reply,"(h)",&handle);g_variant_unref(reply);
    int fd=g_unix_fd_list_get(fds,handle,NULL);g_object_unref(fds);if(fd<0)return FALSE;
    input=capture?ei_new_receiver(NULL):ei_new_sender(NULL);ei_configure_name(input,"DaisyOS Continuity keyboard and mouse");
    if(ei_setup_backend_fd(input,fd)!=0){close(fd);return FALSE;}return TRUE;
}
static gboolean configure_capture(void) {
    GVariant *result=request(CAPTURE,"GetZones","zones",g_variant_new("(o@a{sv})",session,
        g_variant_new_parsed("{'handle_token': <'zones'>}")));
    if(!result)return FALSE;
    guint set=0;g_variant_lookup(result,"zone_set","u",&set);
    GVariant *zones=g_variant_lookup_value(result,"zones",G_VARIANT_TYPE("a(uuii)"));
    if(!zones||!g_variant_n_children(zones)){if(zones)g_variant_unref(zones);g_variant_unref(result);return FALSE;}
    // Use the outside-most zone for the chosen side; never place a barrier on an internal monitor boundary.
    double best=edge_side==0||edge_side==2?INFINITY:-INFINITY;
    for(gsize n=0;n<g_variant_n_children(zones);n++) {
        guint w,h;gint x,y;g_variant_get_child(zones,n,"(uuii)",&w,&h,&x,&y);
        double edge=edge_side==0?(double)x:edge_side==1?(double)x+w:edge_side==2?(double)y:(double)y+h;
        if(w<4||h<4)continue;
        if((edge_side==0||edge_side==2)?edge<best:edge>best){best=edge;zone_x=x;zone_y=y;zone_width=w;zone_height=h;}
    }
    if(!isfinite(best)){g_variant_unref(zones);g_variant_unref(result);return FALSE;}
    GVariantBuilder barriers;g_variant_builder_init(&barriers,G_VARIANT_TYPE("aa{sv}"));capture_zone_count=0;
    for(gsize n=0;n<g_variant_n_children(zones)&&capture_zone_count<64;n++) {
        guint w,h;gint x,y;g_variant_get_child(zones,n,"(uuii)",&w,&h,&x,&y);
        double edge=edge_side==0?(double)x:edge_side==1?(double)x+w:edge_side==2?(double)y:(double)y+h;
        if(w<4||h<4||edge!=best)continue;
        capture_zones[capture_zone_count].x=x;capture_zones[capture_zone_count].y=y;capture_zones[capture_zone_count].w=w;capture_zones[capture_zone_count].h=h;
        guint id=++capture_zone_count;
        gint x1=x,y1=y,x2=x,y2=y;
        if(edge_side<2){x1=x2=(gint)best;y2+=(gint)h-1;}else{y1=y2=(gint)best;x2+=(gint)w-1;}
        GVariantBuilder barrier;g_variant_builder_init(&barrier,G_VARIANT_TYPE_VARDICT);
        g_variant_builder_add(&barrier,"{sv}","barrier_id",g_variant_new_uint32(id));
        g_variant_builder_add(&barrier,"{sv}","position",g_variant_new("(iiii)",x1,y1,x2,y2));
        g_variant_builder_add_value(&barriers,g_variant_builder_end(&barrier));
    }
    g_variant_unref(zones);g_variant_unref(result);
    result=request(CAPTURE,"SetPointerBarriers","barriers",g_variant_new("(o@a{sv}aa{sv}u)",session,
        g_variant_new_parsed("{'handle_token': <'barriers'>}"),&barriers,set));
    if(!result)return FALSE;
    GVariant *failed=g_variant_lookup_value(result,"failed_barriers",G_VARIANT_TYPE("au"));
    gboolean success=failed&&g_variant_n_children(failed)==0;if(failed)g_variant_unref(failed);g_variant_unref(result);
    return success;
}
static void capture_events(void) {
    ei_dispatch(input);struct ei_event *event;
    while((event=ei_get_event(input))) {
        enum ei_event_type type=ei_event_get_type(event);
        if(type==EI_EVENT_DISCONNECT)running=FALSE;
        else if(type==EI_EVENT_SEAT_ADDED)ei_seat_bind_capabilities(ei_event_get_seat(event),EI_DEVICE_CAP_KEYBOARD,EI_DEVICE_CAP_POINTER,EI_DEVICE_CAP_BUTTON,EI_DEVICE_CAP_SCROLL,NULL);
        else if(type==EI_EVENT_KEYBOARD_MODIFIERS)capture_modifier_event(event);
        else if(edge_captured) {
            gboolean ok=TRUE;
            if(type==EI_EVENT_POINTER_MOTION)ok=edge_emit(3,0,0,ei_event_pointer_get_dx(event),ei_event_pointer_get_dy(event));
            else if(type==EI_EVENT_BUTTON_BUTTON)ok=edge_emit(2,ei_event_button_get_button(event),ei_event_button_get_is_press(event),0,0);
            else if(type==EI_EVENT_SCROLL_DELTA)ok=edge_emit(4,0,0,ei_event_scroll_get_dx(event),ei_event_scroll_get_dy(event));
            else if(type==EI_EVENT_SCROLL_DISCRETE)ok=edge_emit(4,0,0,ei_event_scroll_get_discrete_dx(event)/120.0*15,ei_event_scroll_get_discrete_dy(event)/120.0*15);
            else if(type==EI_EVENT_KEYBOARD_KEY) {
                unsigned key=ei_event_keyboard_get_key(event);gboolean pressed=ei_event_keyboard_get_key_is_press(event);
                if(key<768)capture_keys[key]=pressed;
                if(key==1&&pressed&&(capture_keys[29]||capture_keys[97])&&(capture_keys[56]||capture_keys[100])&&(capture_keys[42]||capture_keys[54])) {
                    release_capture(.5);ok=edge_emit(7,0,0,0,0);
                } else ok=edge_emit(1,key,pressed,0,0);
            }
            if(!ok)running=FALSE;
        }
        ei_event_unref(event);
    }
}
static gboolean remote_bounds(void) {
    double x=INFINITY,y=INFINITY,right=-INFINITY,bottom=-INFINITY;gboolean keyboard=FALSE,pointer=FALSE;
    for(int i=0;i<16;i++)if(devices[i]&&resumed[i]) {
        if(ei_device_has_capability(devices[i],EI_DEVICE_CAP_KEYBOARD))keyboard=TRUE;
        if(!ei_device_has_capability(devices[i],EI_DEVICE_CAP_POINTER_ABSOLUTE))continue;
        for(size_t j=0;j<64;j++) {
            struct ei_region *r=ei_device_get_region(devices[i],j);if(!r)break;
            x=fmin(x,ei_region_get_x(r));y=fmin(y,ei_region_get_y(r));right=fmax(right,ei_region_get_x(r)+ei_region_get_width(r));bottom=fmax(bottom,ei_region_get_y(r)+ei_region_get_height(r));pointer=TRUE;
        }
    }
    if(!keyboard||!pointer||right-x<4||bottom-y<4)return FALSE;
    remote_origin_x=x;remote_origin_y=y;remote_position.width=right-x;remote_position.height=bottom-y;return TRUE;
}
static void remote_motion(void) {
    // Map gaps in irregular multi-monitor layouts to the nearest real output.
    double gx=remote_origin_x+remote_position.x,gy=remote_origin_y+remote_position.y,best=INFINITY,bx=0,by=0;
    struct ei_device *device=NULL;
    for(int i=0;i<16;i++)if(devices[i]&&resumed[i]&&ei_device_has_capability(devices[i],EI_DEVICE_CAP_POINTER_ABSOLUTE))
        for(size_t j=0;j<64;j++){
            struct ei_region *r=ei_device_get_region(devices[i],j);if(!r)break;
            double x=edge_clamp(gx,ei_region_get_x(r),ei_region_get_x(r)+ei_region_get_width(r)-1),y=edge_clamp(gy,ei_region_get_y(r),ei_region_get_y(r)+ei_region_get_height(r)-1);
            double distance=(x-gx)*(x-gx)+(y-gy)*(y-gy);if(distance<best){best=distance;bx=x;by=y;device=devices[i];}
        }
    if(device){ei_device_pointer_motion_absolute(device,bx,by);ei_device_frame(device,(uint64_t)g_get_monotonic_time());}
}
static void edge_inject(const char *line) {
    unsigned kind,code,state;double x,y;char extra;
    if(sscanf(line,"%u %u %u %lf %lf %c",&kind,&code,&state,&x,&y,&extra)!=5||!isfinite(x)||!isfinite(y)||fabs(x)>8192||fabs(y)>8192){running=FALSE;return;}
    if(kind==6){if(code>3||x<0||x>1||!remote_bounds()){running=FALSE;return;}stop_emulation();edge_enter(&remote_position,code,x);remote_motion();}
    else if(kind==7){remote_position.active=0;stop_emulation();}
    else if(remote_position.active&&kind==3){double fraction;if(edge_move(&remote_position,x,y,&fraction)){stop_emulation();if(!edge_emit(7,0,0,fraction,0))running=FALSE;}else remote_motion();}
    else if(remote_position.active&&kind>=1&&kind<=4)inject(line);
    else if(kind<1||kind>7)running=FALSE;
}
// Include GLib's wake-up descriptor: portal activation wakes the process
// immediately, while an idle link can sleep instead of polling at 200 Hz.
static int edge_poll(struct pollfd *input_fds) {
    GMainContext *context=g_main_context_default();
    if(!g_main_context_acquire(context))return poll(input_fds,2,5);
    gint priority=0,timeout=0;GPollFD glib_fds[64];struct pollfd fds[66];
    gboolean pending=g_main_context_prepare(context,&priority);
    int count=g_main_context_query(context,priority,&timeout,glib_fds,64);
    if(count>64){g_main_context_release(context);running=FALSE;return -1;}
    fds[0]=input_fds[0];fds[1]=input_fds[1];
    for(int i=0;i<count;i++){fds[i+2].fd=glib_fds[i].fd;fds[i+2].events=glib_fds[i].events;fds[i+2].revents=0;}
    int wait=pending?0:timeout<0?500:timeout>500?500:timeout;
    int result=poll(fds,(nfds_t)(count+2),wait);
    input_fds[0].revents=fds[0].revents;input_fds[1].revents=fds[1].revents;
    for(int i=0;i<count;i++)glib_fds[i].revents=fds[i+2].revents;
    if(g_main_context_check(context,priority,glib_fds,count))g_main_context_dispatch(context);
    g_main_context_release(context);return result;
}
static int edge_main(int argc,char **argv) {
    gboolean capture=argc==3&&!strcmp(argv[1],"--capture");
    if(!capture&&!(argc==2&&!strcmp(argv[1],"--input")))return 2;
    if(capture){const char *sides[]={"left","right","top","bottom"};int found=0;for(unsigned i=0;i<4;i++)if(!strcmp(argv[2],sides[i])){edge_side=i;found=1;}if(!found)return 2;}
    bus=g_bus_get_sync(G_BUS_TYPE_SESSION,NULL,NULL);if(!bus)return 1;
    guint watch=g_dbus_connection_signal_subscribe(bus,DESKTOP,NULL,NULL,NULL,NULL,G_DBUS_SIGNAL_FLAGS_NONE,signal_received,NULL,NULL);
    guint capture_watch=capture?g_dbus_connection_signal_subscribe(bus,DESKTOP,CAPTURE,NULL,PATH,NULL,G_DBUS_SIGNAL_FLAGS_NONE,capture_signal,NULL,NULL):0;
    const char *stage="create session";
    int status=1;if(!create_edge_session(capture))goto cleanup;
    if(!capture) {
        GVariant *result=request("org.freedesktop.portal.RemoteDesktop","SelectDevices","devices",g_variant_new("(o@a{sv})",session,g_variant_new_parsed("{'handle_token': <'devices'>, 'types': <uint32 3>}")));
        if(!result)goto cleanup;g_variant_unref(result);
        result=request("org.freedesktop.portal.RemoteDesktop","Start","start",g_variant_new("(os@a{sv})",session,"",g_variant_new_parsed("{'handle_token': <'start'>}")));
        if(!result)goto cleanup;guint devices_granted=0;g_variant_lookup(result,"devices","u",&devices_granted);g_variant_unref(result);if((devices_granted&3)!=3)goto cleanup;
    }
    stage="connect EIS";if(!connect_edge_eis(capture))goto cleanup;
    stage="configure barriers";if(capture&&!configure_capture())goto cleanup;
    stage="enable capture";if(capture){GVariant *reply=call(CAPTURE,"Enable",g_variant_new("(o@a{sv})",session,empty()));if(!reply)goto cleanup;g_variant_unref(reply);}
    else {
        stage="wait for keyboard and pointer devices";
        gint64 deadline=g_get_monotonic_time()+5*G_USEC_PER_SEC;
        while(running&&!remote_bounds()&&g_get_monotonic_time()<deadline){while(g_main_context_iteration(NULL,FALSE)){}dispatch_input();g_usleep(1000);}
        if(!remote_bounds())goto cleanup;
    }
    if(!write_frame(NULL,0))goto cleanup;
    fcntl(STDIN_FILENO,F_SETFL,O_NONBLOCK);char buffer[1024];size_t used=0;gint64 last=g_get_monotonic_time();
    while(running&&!g_dbus_connection_is_closed(bus)) {
        while(g_main_context_iteration(NULL,FALSE)){}if(!running)break;
        if(capture){
            if(edge_rearm){
                edge_rearm=FALSE;
                if(!edge_suspended){GVariant *r=call(CAPTURE,"Enable",g_variant_new("(o@a{sv})",session,empty()));if(!r)break;g_variant_unref(r);}
            }
            capture_events();
        }else {
            double width=remote_position.width,height=remote_position.height,x=remote_origin_x,y=remote_origin_y;
            dispatch_input();
            if(!remote_bounds()||width!=remote_position.width||height!=remote_position.height||x!=remote_origin_x||y!=remote_origin_y)break;
        }
        struct pollfd fds[2]={{STDIN_FILENO,POLLIN|POLLHUP,0},{ei_get_fd(input),POLLIN,0}};
        if(edge_poll(fds)<0||!running)break;if(fds[0].revents&(POLLHUP|POLLERR))break;
        if(fds[0].revents&POLLIN) {
            ssize_t n=read(STDIN_FILENO,buffer+used,sizeof buffer-used-1);if(n<=0)break;used+=(size_t)n;buffer[used]=0;char *newline;
            while((newline=strchr(buffer,'\n'))) {
                *newline=0;
                if(capture){
                    if(!strncmp(buffer,"release ",8)){char *end;double fraction=strtod(buffer+8,&end);if(*end||!isfinite(fraction)){running=FALSE;break;}release_capture(fraction);}
                    else if(!strcmp(buffer,"suspend")){if(edge_captured){release_capture(.5);if(!edge_emit(7,0,0,0,0))running=FALSE;}edge_suspended=TRUE;GVariant *r=call(CAPTURE,"Disable",g_variant_new("(o@a{sv})",session,empty()));if(r)g_variant_unref(r);edge_suspended=TRUE;}
                    else if(!strcmp(buffer,"resume")&&edge_suspended){GVariant *r=call(CAPTURE,"Enable",g_variant_new("(o@a{sv})",session,empty()));if(r)g_variant_unref(r);edge_suspended=FALSE;}
                    else running=FALSE;
                }else edge_inject(buffer);
                size_t consumed=(size_t)(newline-buffer)+1;memmove(buffer,buffer+consumed,used-consumed);used-=consumed;buffer[used]=0;
            }
            if(used==sizeof buffer-1)break;
        }
        if(g_get_monotonic_time()-last>2*G_USEC_PER_SEC){if(!write_frame(NULL,0))break;last=g_get_monotonic_time();}
    }
    status=0;
cleanup:
    if(status)g_printerr("Continuity input initialization failed: %s\n",stage);
    if(capture)release_capture(.5);else if(input)stop_emulation();
    if(session){GVariant *r=g_dbus_connection_call_sync(bus,DESKTOP,session,"org.freedesktop.portal.Session","Close",NULL,NULL,G_DBUS_CALL_FLAGS_NONE,2000,NULL,NULL);if(r)g_variant_unref(r);g_free(session);}
    for(int i=0;i<16;i++)if(devices[i])ei_device_unref(devices[i]);if(input)ei_unref(input);
    if(capture_map)xkb_keymap_unref(capture_map);if(capture_ei_map)ei_keymap_unref(capture_ei_map);if(capture_xkb)xkb_context_unref(capture_xkb);
    if(capture_watch)g_dbus_connection_signal_unsubscribe(bus,capture_watch);g_dbus_connection_signal_unsubscribe(bus,watch);g_object_unref(bus);return status;
}
