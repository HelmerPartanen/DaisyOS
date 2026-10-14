// Live Wayland integration check: a transparent test host above its video child.
#include "mpv_wallpaper.h"
#include "xdg-shell-client.h"
#include <wayland-client.h>
#include <algorithm>
#include <chrono>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <thread>
#include <sys/mman.h>
#include <unistd.h>
struct Host {
    wl_display *display=nullptr;
    wl_compositor *compositor=nullptr;
    wl_shm *shm=nullptr;
    xdg_wm_base *shell=nullptr;
    wl_surface *surface=nullptr;
    wl_buffer *buffer=nullptr;
    bool configured=false,closed=false;
    static void global(void *data,wl_registry *reg,uint32_t id,const char *name,uint32_t version) {
        auto h=static_cast<Host*>(data);
        if(!strcmp(name,"wl_compositor")) h->compositor=static_cast<wl_compositor*>(wl_registry_bind(reg,id,&wl_compositor_interface,std::min(version,4u)));
        if(!strcmp(name,"wl_shm")) h->shm=static_cast<wl_shm*>(wl_registry_bind(reg,id,&wl_shm_interface,1));
        if(!strcmp(name,"xdg_wm_base")) h->shell=static_cast<xdg_wm_base*>(wl_registry_bind(reg,id,&xdg_wm_base_interface,1));
    }
    static void removed(void *,wl_registry *,uint32_t) {}
    static void ping(void *,xdg_wm_base *shell,uint32_t serial) { xdg_wm_base_pong(shell,serial); }
    static void configure(void *data,xdg_surface *surface,uint32_t serial) {
        auto h=static_cast<Host*>(data); xdg_surface_ack_configure(surface,serial);
        wl_surface_attach(h->surface,h->buffer,0,0); wl_surface_damage(h->surface,0,0,960,540); wl_surface_commit(h->surface); h->configured=true;
    }
    static void top_configure(void *,xdg_toplevel *,int32_t,int32_t,wl_array *) {}
    static void close(void *data,xdg_toplevel *) { static_cast<Host*>(data)->closed=true; }
};
int main(int argc,char **argv) {
    if(argc<2) { std::fprintf(stderr,"Usage: %s /absolute/video [seconds=30] [hdr=0|1|2]\n",argv[0]); return 1; }
    const int seconds=argc>2 ? std::atoi(argv[2]) : 30;
    Host h; h.display=wl_display_connect(nullptr); if(!h.display) return 2;
    auto registry=wl_display_get_registry(h.display);
    static const wl_registry_listener rl={Host::global,Host::removed}; wl_registry_add_listener(registry,&rl,&h); wl_display_roundtrip(h.display);
    if(!h.compositor || !h.shm || !h.shell) return 3;
    static const xdg_wm_base_listener sl={Host::ping}; xdg_wm_base_add_listener(h.shell,&sl,&h);
    h.surface=wl_compositor_create_surface(h.compositor);
    int fd=memfd_create("daisy-video-test",MFD_CLOEXEC); size_t bytes=960*540*4;
    if(fd<0 || ftruncate(fd,bytes)<0) return 4;
    auto pixels=mmap(nullptr,bytes,PROT_READ|PROT_WRITE,MAP_SHARED,fd,0); memset(pixels,0,bytes);
    auto pool=wl_shm_create_pool(h.shm,fd,bytes); h.buffer=wl_shm_pool_create_buffer(pool,0,960,540,960*4,WL_SHM_FORMAT_ARGB8888); wl_shm_pool_destroy(pool); close(fd);
    auto surface=xdg_wm_base_get_xdg_surface(h.shell,h.surface);
    static const xdg_surface_listener xl={Host::configure}; xdg_surface_add_listener(surface,&xl,&h);
    auto top=xdg_surface_get_toplevel(surface);
    static const xdg_toplevel_listener tl={Host::top_configure,Host::close,nullptr,nullptr}; xdg_toplevel_add_listener(top,&tl,&h);
    xdg_toplevel_set_title(top,"DaisyOS Video Pipeline Test"); wl_surface_commit(h.surface);
    while(!h.configured && wl_display_dispatch(h.display)>=0) {}
    DaisyMpvColor color{}; color.hdr=argc>3 ? std::atoi(argv[3]) : 0; color.peak_nits=1000; color.start_seconds=argc>4 ? std::atof(argv[4]) : 0;
    auto player=daisy_mpv_create(h.display,h.surface,argv[1],960,540,1,&color); if(!player) return 5;
    auto start=std::chrono::steady_clock::now(); int last_second=-1; bool failed=false; int64_t paused_submitted=0;
    while(!h.closed) {
        int elapsed=std::chrono::duration_cast<std::chrono::seconds>(std::chrono::steady_clock::now()-start).count();
        if(elapsed>=seconds) break;
        // Reading the shared connection also queues the child's private events.
        if(wl_display_roundtrip(h.display)<0) { failed=true;break; }
        wl_surface_commit(h.surface); wl_display_flush(h.display);
        DaisyMpvStats stats{}; daisy_mpv_stats(player,&stats);
        if(stats.state==3) { std::fprintf(stderr,"FAIL: %s\n",stats.message);failed=true;break; }
        if(elapsed!=last_second) {
            std::printf("t=%d state=%d hdr=%d managed=%d depth=%d submitted=%lld presented=%lld discarded=%lld max_interval_us=%lld position=%.3f\n",elapsed,stats.state,stats.hdr_active,stats.color_managed,stats.buffer_depth,(long long)stats.submitted,(long long)stats.presented,(long long)stats.discarded,(long long)stats.max_interval_us,stats.position_seconds); fflush(stdout);
            last_second=elapsed;
            if(elapsed==10) { daisy_mpv_pause(player,1); }
            if(elapsed==11) paused_submitted=stats.submitted;
            if(elapsed==12 && stats.submitted!=paused_submitted) { std::fprintf(stderr,"FAIL: rendered while paused\n");failed=true;break; }
            if(elapsed==13) daisy_mpv_pause(player,0);
            if(elapsed==15) daisy_mpv_resize(player,800,450,1);
        }
        std::this_thread::sleep_for(std::chrono::milliseconds(5));
    }
    DaisyMpvStats stats{}; daisy_mpv_stats(player,&stats);
    if(stats.presented==0 || stats.submitted>int64_t(seconds)*61 || (color.hdr && !stats.hdr_active)) failed=true;
    daisy_mpv_destroy(player);
    xdg_toplevel_destroy(top); xdg_surface_destroy(surface); wl_surface_destroy(h.surface); wl_buffer_destroy(h.buffer); munmap(pixels,bytes);
    xdg_wm_base_destroy(h.shell); wl_shm_destroy(h.shm); wl_compositor_destroy(h.compositor); wl_registry_destroy(registry); wl_display_disconnect(h.display);
    return failed ? 6 : 0;
}
