// SPDX-License-Identifier: MIT
// DaisyOS owns selection/consent; this process owns one compositor PipeWire feed.
#include <wayland-client.h>
#include <poll.h>
#include <stdbool.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>
#include "screencast-client.h"
#include "xdg-output-client.h"
struct output { struct wl_output *proxy; char name[256]; int x,y,width,height,scale,transform; int logical_x,logical_y,logical_width,logical_height; bool logical_position,logical_size; };
static struct output outputs[64]; static int count;
static struct zkde_screencast_unstable_v1 *manager;
static struct zxdg_output_manager_v1 *logical_manager;
static bool running=true;
static void geometry(void *data,struct wl_output *o,int32_t x,int32_t y,int32_t pw,int32_t ph,int32_t sub,const char *make,const char *model,int32_t transform) { (void)o;(void)pw;(void)ph;(void)sub;(void)make;(void)model;struct output *v=data;v->x=x;v->y=y;v->transform=transform; }
static void mode(void *data,struct wl_output *o,uint32_t flags,int32_t w,int32_t h,int32_t refresh) { (void)o;(void)refresh;if(flags & WL_OUTPUT_MODE_CURRENT){struct output *v=data;v->width=w;v->height=h;} }
static void done(void *data,struct wl_output *o) { (void)data;(void)o; }
static void scale(void *data,struct wl_output *o,int32_t s) { (void)o;((struct output*)data)->scale=s>0?s:1; }
static void name(void *data,struct wl_output *o,const char *n) { (void)o;snprintf(((struct output*)data)->name,256,"%s",n); }
static void description(void *data,struct wl_output *o,const char *n) { (void)data;(void)o;(void)n; }
static const struct wl_output_listener output_listener={geometry,mode,done,scale,name,description};
// wl_output.scale is integral and cannot describe fractional display scaling.
// xdg-output reports the actual compositor layout coordinates used by barriers.
static void logical_position(void *data,struct zxdg_output_v1 *o,int32_t x,int32_t y){(void)o;struct output *v=data;v->logical_x=x;v->logical_y=y;v->logical_position=true;}
static void logical_size(void *data,struct zxdg_output_v1 *o,int32_t w,int32_t h){(void)o;struct output *v=data;v->logical_width=w;v->logical_height=h;v->logical_size=w>0&&h>0;}
static void logical_done(void *data,struct zxdg_output_v1 *o){(void)data;(void)o;}
static void logical_name(void *data,struct zxdg_output_v1 *o,const char *n){(void)o;snprintf(((struct output*)data)->name,256,"%s",n);}
static void logical_description(void *data,struct zxdg_output_v1 *o,const char *n){(void)data;(void)o;(void)n;}
static const struct zxdg_output_v1_listener logical_listener={logical_position,logical_size,logical_done,logical_name,logical_description};
static void global(void *data,struct wl_registry *r,uint32_t id,const char *iface,uint32_t version) { (void)data;
 if(!strcmp(iface,"wl_output")&&count<64&&version>=4){struct output *v=&outputs[count++];v->scale=1;v->proxy=wl_registry_bind(r,id,&wl_output_interface,4);wl_output_add_listener(v->proxy,&output_listener,v);}
 if(!strcmp(iface,"zxdg_output_manager_v1"))logical_manager=wl_registry_bind(r,id,&zxdg_output_manager_v1_interface,version<3?version:3);
 if(!strcmp(iface,"zkde_screencast_unstable_v1"))manager=wl_registry_bind(r,id,&zkde_screencast_unstable_v1_interface,version<3?version:3);
}
static void removed(void *d,struct wl_registry *r,uint32_t id){(void)d;(void)r;(void)id;}
static const struct wl_registry_listener registry_listener={global,removed};
static void closed(void *d,struct zkde_screencast_stream_unstable_v1 *s){(void)d;(void)s;puts("closed");fflush(stdout);running=false;}
static void created(void *d,struct zkde_screencast_stream_unstable_v1 *s,uint32_t node){(void)d;(void)s;printf("node %u\n",node);fflush(stdout);}
static void failed(void *d,struct zkde_screencast_stream_unstable_v1 *s,const char *error){(void)d;(void)s;fprintf(stderr,"capture: %s\n",error);running=false;}
static const struct zkde_screencast_stream_unstable_v1_listener stream_listener={closed,created,failed};
int main(int argc,char **argv){
 struct wl_display *display=wl_display_connect(NULL);if(!display){fputs("No Wayland display\n",stderr);return 1;}
 struct wl_registry *registry=wl_display_get_registry(display);wl_registry_add_listener(registry,&registry_listener,NULL);
 if(wl_display_roundtrip(display)<0||wl_display_roundtrip(display)<0)return 1;
 if(logical_manager){
  for(int i=0;i<count;i++){struct zxdg_output_v1 *logical=zxdg_output_manager_v1_get_xdg_output(logical_manager,outputs[i].proxy);zxdg_output_v1_add_listener(logical,&logical_listener,&outputs[i]);}
  if(wl_display_roundtrip(display)<0||wl_display_roundtrip(display)<0)return 1;
 }
 if(argc==2&&!strcmp(argv[1],"--list")){
  printf("capture %d\n",manager!=NULL);
  for(int i=0;i<count;i++){
   struct output *v=&outputs[i];bool logical=v->logical_position&&v->logical_size;
   if(v->name[0])printf("output\t%s\t%d\t%d\t%d\t%d\n",v->name,logical?v->logical_x:v->x,logical?v->logical_y:v->y,
    logical?v->logical_width:(v->transform%2?v->height:v->width)/v->scale,
    logical?v->logical_height:(v->transform%2?v->width:v->height)/v->scale);
  }
  wl_display_disconnect(display);return manager?0:2;
 }
 if(argc!=4||!manager){fputs("Usage: daisyos-portal-capture --output NAME CURSOR | --window UUID CURSOR\n",stderr);return 2;}
 char *end=NULL;unsigned long cursor=strtoul(argv[3],&end,10);if(!end||*end||!(cursor==1||cursor==2||cursor==4))return 2;
 struct zkde_screencast_stream_unstable_v1 *stream=NULL;
 if(!strcmp(argv[1],"--window"))stream=zkde_screencast_unstable_v1_stream_window(manager,argv[2],(uint32_t)cursor);
 else if(!strcmp(argv[1],"--output")){for(int i=0;i<count;i++)if(!strcmp(outputs[i].name,argv[2]))stream=zkde_screencast_unstable_v1_stream_output(manager,outputs[i].proxy,(uint32_t)cursor);}
 if(!stream){fputs("The selected source is unavailable\n",stderr);return 2;}
 zkde_screencast_stream_unstable_v1_add_listener(stream,&stream_listener,NULL);
 while(running){
  if(wl_display_dispatch_pending(display)<0||wl_display_flush(display)<0)break;
  struct pollfd fds[2]={{wl_display_get_fd(display),POLLIN,0},{STDIN_FILENO,POLLIN,0}};
  if(poll(fds,2,-1)<0)break;
  if(fds[1].revents){char command[16];(void)read(STDIN_FILENO,command,sizeof command);break;}
  if(fds[0].revents && wl_display_dispatch(display)<0)break;
 }
 zkde_screencast_stream_unstable_v1_close(stream);wl_display_flush(display);wl_display_disconnect(display);return 0;
}
