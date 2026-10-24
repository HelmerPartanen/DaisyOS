#include "mpv_wallpaper.h"
#include <mpv/client.h>
#include <mpv/render_gl.h>
extern "C" {
#include <libavformat/avformat.h>
#include <libavutil/pixfmt.h>
#include <libavutil/pixdesc.h>
}
#include <wayland-client.h>
#include <wayland-egl.h>
#include <EGL/egl.h>
#include <EGL/eglext.h>
#include <GL/gl.h>
#include "color-management-v1-client.h"
#include "presentation-time-client.h"
#include "viewporter-client.h"
#include <algorithm>
#include <atomic>
#include <chrono>
#include <condition_variable>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <mutex>
#include <set>
#include <stdexcept>
#include <string>
#include <thread>
#include <vector>

namespace {
using Clock = std::chrono::steady_clock;
class Player {
public:
    wl_display *display;
    wl_surface *parent;
    std::string path;
    std::string source_filter;
    bool hardware_suitable=true;
    int source_depth=8;
    uint32_t source_sdr_primary=WP_COLOR_MANAGER_V1_PRIMARIES_SRGB;
    uint32_t sdr_primary=WP_COLOR_MANAGER_V1_PRIMARIES_SRGB;
    std::string sdr_target="bt.709";
    DaisyMpvColor color{};
    std::atomic<int> fps_cap{60};
    std::atomic<int> width, height;
    std::atomic<double> scale;
    std::atomic<bool> stopping{false}, paused{false}, dirty{false}, throttled{false};
    std::atomic<int> state{0};
    std::atomic<double> position{0};
    std::atomic<int64_t> submitted{0}, presented{0}, discarded{0}, last_present{0}, max_interval{0}, engine_dropped{0};
    std::atomic<bool> hdr{false}, managed{false};
    std::mutex mutex;
    std::condition_variable cv;
    std::string message;
    bool surface_ready=false, core_ready=false, renderer_ready=false;
    mpv_handle *core=nullptr;
    mpv_render_context *renderer=nullptr;
    std::thread render_thread, control_thread;
    // Wayland objects use a private queue; only the render worker dispatches it.
    wl_event_queue *queue=nullptr;
    wl_registry *registry=nullptr;
    wl_compositor *compositor=nullptr;
    wl_subcompositor *subcompositor=nullptr;
    wl_surface *surface=nullptr;
    wl_subsurface *subsurface=nullptr;
    wp_viewporter *viewporter=nullptr;
    wp_viewport *viewport=nullptr;
    wp_presentation *presentation=nullptr;
    wp_color_manager_v1 *colors=nullptr;
    wp_color_management_surface_v1 *color_surface=nullptr;
    wp_image_description_v1 *description=nullptr;
    std::set<uint32_t> features, transfers, primaries;
    bool description_ready=false;
    wl_callback *frame_callback=nullptr;
    Clock::time_point frame_requested;
    std::set<struct wp_presentation_feedback*> feedbacks;
    EGLDisplay egl_display=EGL_NO_DISPLAY;
    EGLContext egl_context=EGL_NO_CONTEXT;
    EGLSurface egl_surface=EGL_NO_SURFACE;
    wl_egl_window *egl_window=nullptr;
    int depth=8;
    std::atomic<int> reported_depth{8};
    std::vector<unsigned char> snapshot;

    Player(void *d, void *p, const char *file, int w, int h, double s, const DaisyMpvColor *c)
        :display(static_cast<wl_display*>(d)),parent(static_cast<wl_surface*>(p)),path(file),width(w),height(h),scale(s) {
        if (c) color=*c;
        if (w<1 || h<1 || w>7680 || h>7680 || s<=0 || s>8) throw std::runtime_error("Invalid video surface dimensions");
        render_thread=std::thread([this]{ render_loop(); });
        control_thread=std::thread([this]{ control_loop(); });
    }
    ~Player() {
        stopping=true; cv.notify_all();
        if(render_thread.joinable()) render_thread.join();
        if(control_thread.joinable()) control_thread.join();
        if(core) mpv_terminate_destroy(core);
    }
    void fail(const char *error) {
        { std::lock_guard<std::mutex> lock(mutex); message=error; }
        state=3; cv.notify_all();
        std::fprintf(stderr,"[wallpaper-mpv] %s\n",error);
    }
    static void update(void *data) { auto p=static_cast<Player*>(data); p->dirty=true; p->cv.notify_all(); }
    static void *proc(void *,const char *name) { return reinterpret_cast<void*>(eglGetProcAddress(name)); }
    template<typename T> void own_queue(T *object) { wl_proxy_set_queue(reinterpret_cast<wl_proxy*>(object),queue); }
    static void global(void *data,wl_registry *reg,uint32_t name,const char *iface,uint32_t version) {
        auto p=static_cast<Player*>(data);
#define BIND(type, field, ver) if(std::strcmp(iface,#type)==0) { p->field=static_cast<type*>(wl_registry_bind(reg,name,&type##_interface,std::min(version,uint32_t(ver)))); p->own_queue(p->field); }
        BIND(wl_compositor,compositor,4)
        else BIND(wl_subcompositor,subcompositor,1)
        else BIND(wp_viewporter,viewporter,1)
        else BIND(wp_presentation,presentation,1)
        else BIND(wp_color_manager_v1,colors,2)
#undef BIND
    }
    static void global_remove(void *,wl_registry *,uint32_t) {}
    static void color_intent(void *,wp_color_manager_v1 *,uint32_t) {}
    static void color_feature(void *data,wp_color_manager_v1 *,uint32_t value) { static_cast<Player*>(data)->features.insert(value); }
    static void color_transfer(void *data,wp_color_manager_v1 *,uint32_t value) { static_cast<Player*>(data)->transfers.insert(value); }
    static void color_primary(void *data,wp_color_manager_v1 *,uint32_t value) { static_cast<Player*>(data)->primaries.insert(value); }
    static void color_done(void *,wp_color_manager_v1 *) {}
    static void desc_failed(void *data,wp_image_description_v1 *,uint32_t,const char *msg) { std::fprintf(stderr,"[wallpaper-mpv] color description unavailable: %s\n",msg); static_cast<Player*>(data)->description_ready=false; }
    static void desc_ready(void *data,wp_image_description_v1 *,uint32_t) { static_cast<Player*>(data)->description_ready=true; }
    static void desc_ready2(void *data,wp_image_description_v1 *,uint32_t,uint32_t) { static_cast<Player*>(data)->description_ready=true; }
    bool describe(bool want_hdr) {
        if(!colors || !features.count(WP_COLOR_MANAGER_V1_FEATURE_PARAMETRIC)) return false;
        auto tf=want_hdr ? WP_COLOR_MANAGER_V1_TRANSFER_FUNCTION_ST2084_PQ : WP_COLOR_MANAGER_V1_TRANSFER_FUNCTION_GAMMA22;
        const uint32_t prim=want_hdr ? uint32_t(WP_COLOR_MANAGER_V1_PRIMARIES_BT2020) : sdr_primary;
        if(!transfers.count(tf) || !primaries.count(prim)) return false;
        auto creator=wp_color_manager_v1_create_parametric_creator(colors); own_queue(creator);
        wp_image_description_creator_params_v1_set_tf_named(creator,tf);
        wp_image_description_creator_params_v1_set_primaries_named(creator,prim);
        if(want_hdr && features.count(WP_COLOR_MANAGER_V1_FEATURE_SET_LUMINANCES))
            wp_image_description_creator_params_v1_set_luminances(creator,0,10000,203);
        if(want_hdr && features.count(WP_COLOR_MANAGER_V1_FEATURE_SET_MASTERING_DISPLAY_PRIMARIES)) {
            if(color.primaries[0]>0)
                wp_image_description_creator_params_v1_set_mastering_display_primaries(creator,
                    color.primaries[0],color.primaries[1],color.primaries[2],color.primaries[3],
                    color.primaries[4],color.primaries[5],color.primaries[6],color.primaries[7]);
            wp_image_description_creator_params_v1_set_mastering_luminance(creator,
                std::max(0,color.min_luminance),std::clamp(color.peak_nits,1,10000));
        }
        if(want_hdr) {
            if(color.max_cll>0) wp_image_description_creator_params_v1_set_max_cll(creator,color.max_cll);
            if(color.max_fall>0) wp_image_description_creator_params_v1_set_max_fall(creator,color.max_fall);
        }
        description=wp_image_description_creator_params_v1_create(creator); own_queue(description);
        static const wp_image_description_v1_listener listener={desc_failed,desc_ready,desc_ready2};
        wp_image_description_v1_add_listener(description,&listener,this);
        if(wl_display_roundtrip_queue(display,queue)<0) return false;
        if(!description_ready) { wp_image_description_v1_destroy(description); description=nullptr; return false; }
        color_surface=wp_color_manager_v1_get_surface(colors,surface); own_queue(color_surface);
        wp_color_management_surface_v1_set_image_description(color_surface,description,WP_COLOR_MANAGER_V1_RENDER_INTENT_PERCEPTUAL);
        return true;
    }
    void probe_source_color() {
        AVFormatContext *format=nullptr;
        AVDictionary *options=nullptr;
        av_dict_set(&options,"protocol_whitelist","file,crypto,data",0);
        int result=avformat_open_input(&format,path.c_str(),nullptr,&options);
        av_dict_free(&options);
        if(result<0) throw std::runtime_error("Cannot probe local video color metadata");
        result=avformat_find_stream_info(format,nullptr);
        if(result<0) { avformat_close_input(&format); throw std::runtime_error("Cannot read video metadata"); }
        int index=av_find_best_stream(format,AVMEDIA_TYPE_VIDEO,-1,-1,nullptr,0);
        if(index<0) { avformat_close_input(&format); throw std::runtime_error("No video stream"); }
        auto params=format->streams[index]->codecpar;
        hardware_suitable=params->width>=48 && params->height>=16;
        if(auto pixel=av_pix_fmt_desc_get(static_cast<AVPixelFormat>(params->format))) source_depth=pixel->comp[0].depth;
        if(params->color_primaries==AVCOL_PRI_SMPTE432) source_sdr_primary=WP_COLOR_MANAGER_V1_PRIMARIES_DISPLAY_P3;
        if(params->color_primaries==AVCOL_PRI_BT2020) source_sdr_primary=WP_COLOR_MANAGER_V1_PRIMARIES_BT2020;
        const char *prim=nullptr,*transfer=nullptr,*matrix=nullptr,*levels=nullptr;
        switch(params->color_primaries) {
            case AVCOL_PRI_BT709: prim="bt.709"; break;
            case AVCOL_PRI_BT2020: prim="bt.2020"; break;
            case AVCOL_PRI_SMPTE432: prim="display-p3"; break;
            case AVCOL_PRI_SMPTE431: prim="dci-p3"; break;
            case AVCOL_PRI_SMPTE170M: prim="bt.601-525"; break;
            case AVCOL_PRI_BT470BG: prim="bt.601-625"; break;
            default: break;
        }
        switch(params->color_trc) {
            case AVCOL_TRC_GAMMA22: transfer="gamma2.2"; break;
            case AVCOL_TRC_GAMMA28: transfer="gamma2.8"; break;
            case AVCOL_TRC_IEC61966_2_1: transfer="srgb"; break;
            case AVCOL_TRC_LINEAR: transfer="linear"; break;
            case AVCOL_TRC_SMPTE2084: transfer="pq"; color.hdr=1; break;
            case AVCOL_TRC_ARIB_STD_B67: transfer="hlg"; color.hdr=2; break;
            // Standard television SDR is interpreted in its display-referred
            // BT.1886 space, matching mpv's normal video presentation policy.
            case AVCOL_TRC_BT709:
            case AVCOL_TRC_SMPTE170M:
            case AVCOL_TRC_BT2020_10:
            case AVCOL_TRC_BT2020_12: transfer="bt.1886"; break;
            default: break;
        }
        switch(params->color_space) {
            case AVCOL_SPC_BT709: matrix="bt.709"; break;
            case AVCOL_SPC_BT470BG:
            case AVCOL_SPC_SMPTE170M: matrix="bt.601"; break;
            case AVCOL_SPC_BT2020_NCL: matrix="bt.2020-ncl"; break;
            case AVCOL_SPC_BT2020_CL: matrix="bt.2020-cl"; break;
            default: break;
        }
        if(params->color_range==AVCOL_RANGE_JPEG) levels="full";
        if(params->color_range==AVCOL_RANGE_MPEG) levels="limited";
        // Explicit container tags (e.g. QuickTime colr) take precedence over
        // conflicting codec-frame VUI tags. format changes metadata only;
        // the GPU renderer performs the single actual color transformation.
        auto add=[&](const char *name,const char *value) {
            if(!value) return;
            source_filter += source_filter.empty() ? "format=" : ":";
            source_filter += std::string(name)+"="+value;
        };
        add("primaries",prim); add("gamma",transfer); add("colormatrix",matrix); add("colorlevels",levels);
        std::fprintf(stderr,"[wallpaper-mpv] source color: %s\n",source_filter.c_str());
        avformat_close_input(&format);
    }
    void init_surface() {
        if(display) {
            queue=wl_display_create_queue(display);
            registry=wl_display_get_registry(display); own_queue(registry);
            static const wl_registry_listener listener={global,global_remove};
            wl_registry_add_listener(registry,&listener,this);
            if(wl_display_roundtrip_queue(display,queue)<0 || !compositor || !subcompositor || !viewporter)
                throw std::runtime_error("Wayland wallpaper child protocols unavailable");
            if(colors) {
                static const wp_color_manager_v1_listener cl={color_intent,color_feature,color_transfer,color_primary,color_done};
                wp_color_manager_v1_add_listener(colors,&cl,this);
                if(wl_display_roundtrip_queue(display,queue)<0) throw std::runtime_error("Wayland color negotiation failed");
            }
            surface=wl_compositor_create_surface(compositor); own_queue(surface);
            subsurface=wl_subcompositor_get_subsurface(subcompositor,surface,parent); own_queue(subsurface);
            wl_subsurface_place_below(subsurface,parent);
            wl_subsurface_set_desync(subsurface);
            auto region=wl_compositor_create_region(compositor);
            wl_surface_set_input_region(surface,region); wl_region_destroy(region);
            viewport=wp_viewporter_get_viewport(viewporter,surface); own_queue(viewport);
            wp_viewport_set_destination(viewport,width,height);
            egl_display=eglGetDisplay(reinterpret_cast<EGLNativeDisplayType>(display));
        } else {
            auto platform=reinterpret_cast<PFNEGLGETPLATFORMDISPLAYEXTPROC>(eglGetProcAddress("eglGetPlatformDisplayEXT"));
            egl_display=platform ? platform(EGL_PLATFORM_SURFACELESS_MESA,EGL_DEFAULT_DISPLAY,nullptr) : eglGetDisplay(EGL_DEFAULT_DISPLAY);
        }
        EGLint major,minor;
        if(egl_display==EGL_NO_DISPLAY || !eglInitialize(egl_display,&major,&minor) || !eglBindAPI(EGL_OPENGL_API))
            throw std::runtime_error("Wallpaper EGL initialization failed");
        bool want_hdr=display && color.hdr && colors && features.count(WP_COLOR_MANAGER_V1_FEATURE_PARAMETRIC)
            && transfers.count(WP_COLOR_MANAGER_V1_TRANSFER_FUNCTION_ST2084_PQ) && primaries.count(WP_COLOR_MANAGER_V1_PRIMARIES_BT2020);
        if(display && colors && features.count(WP_COLOR_MANAGER_V1_FEATURE_PARAMETRIC) &&
           transfers.count(WP_COLOR_MANAGER_V1_TRANSFER_FUNCTION_GAMMA22) && primaries.count(source_sdr_primary)) {
            sdr_primary=source_sdr_primary;
            if(sdr_primary==WP_COLOR_MANAGER_V1_PRIMARIES_DISPLAY_P3) sdr_target="display-p3";
            if(sdr_primary==WP_COLOR_MANAGER_V1_PRIMARIES_BT2020) sdr_target="bt.2020";
        }
        bool want_deep=want_hdr || (display && source_depth>8 && colors && features.count(WP_COLOR_MANAGER_V1_FEATURE_PARAMETRIC));
        EGLConfig config; EGLint count=0;
        std::fprintf(stderr,"[wallpaper-mpv] HDR requested=%d PQ=%d BT2020=%d parametric=%d\n",color.hdr,
            int(transfers.count(WP_COLOR_MANAGER_V1_TRANSFER_FUNCTION_ST2084_PQ)),
            int(primaries.count(WP_COLOR_MANAGER_V1_PRIMARIES_BT2020)),
            int(features.count(WP_COLOR_MANAGER_V1_FEATURE_PARAMETRIC)));
        for(int attempt=0;attempt<3;attempt++) {
            depth=want_deep ? (attempt==0 ? 10 : 16) : 8;
            const char *extensions=eglQueryString(egl_display,EGL_EXTENSIONS);
            const bool float_formats=extensions && std::strstr(extensions,"EGL_EXT_pixel_format_float");
            if(depth==16 && !float_formats) { want_hdr=false; want_deep=false; continue; }
            std::vector<EGLint> attrs={EGL_SURFACE_TYPE,display ? EGL_WINDOW_BIT : EGL_PBUFFER_BIT,EGL_RENDERABLE_TYPE,EGL_OPENGL_BIT,
                EGL_RED_SIZE,depth,EGL_GREEN_SIZE,depth,EGL_BLUE_SIZE,depth,EGL_ALPHA_SIZE,depth==10 ? 2 : depth,
                EGL_NONE};
            if(float_formats) {
                attrs.pop_back(); attrs.push_back(EGL_COLOR_COMPONENT_TYPE_EXT);
                attrs.push_back(depth==16 ? EGL_COLOR_COMPONENT_TYPE_FLOAT_EXT : EGL_COLOR_COMPONENT_TYPE_FIXED_EXT);
                attrs.push_back(EGL_NONE);
            }
            if(eglChooseConfig(egl_display,attrs.data(),&config,1,&count) && count>0) break;
            if(want_deep && depth==10) {
                // NVIDIA Wayland may offer FP16 windows but no RGB10A2.
                // FP16 retains HDR precision without passing through SDR UI.
                std::fprintf(stderr,"[wallpaper-mpv] Trying FP16 video surface\n");
                continue;
            }
            std::fprintf(stderr,"[wallpaper-mpv] HDR pixel formats unavailable; using SDR\n");
            want_hdr=false; want_deep=false;
        }
        if(count<1) throw std::runtime_error("No wallpaper EGL pixel format");
        EGLint red=0; eglGetConfigAttrib(egl_display,config,EGL_RED_SIZE,&red);
        want_hdr=want_hdr && red>=10;
        managed=display && describe(want_hdr);
        if(display && !managed) {
            want_hdr=false;
            sdr_primary=WP_COLOR_MANAGER_V1_PRIMARIES_SRGB; sdr_target="bt.709";
            managed=describe(false);
        }
        hdr=want_hdr;
        reported_depth=depth;
        EGLint context_attrs[]={EGL_CONTEXT_MAJOR_VERSION,3,EGL_CONTEXT_MINOR_VERSION,3,EGL_CONTEXT_OPENGL_PROFILE_MASK,EGL_CONTEXT_OPENGL_CORE_PROFILE_BIT,EGL_NONE};
        egl_context=eglCreateContext(egl_display,config,EGL_NO_CONTEXT,context_attrs);
        if(egl_context==EGL_NO_CONTEXT) egl_context=eglCreateContext(egl_display,config,EGL_NO_CONTEXT,nullptr);
        if(display) {
            egl_window=wl_egl_window_create(surface,std::max(1,int(width*scale)),std::max(1,int(height*scale)));
            egl_surface=eglCreateWindowSurface(egl_display,config,reinterpret_cast<EGLNativeWindowType>(egl_window),nullptr);
        } else {
            EGLint attrs[]={EGL_WIDTH,width,EGL_HEIGHT,height,EGL_NONE};
            egl_surface=eglCreatePbufferSurface(egl_display,config,attrs);
        }
        if(egl_context==EGL_NO_CONTEXT || egl_surface==EGL_NO_SURFACE || !eglMakeCurrent(egl_display,egl_surface,egl_surface,egl_context))
            throw std::runtime_error("Wallpaper EGL surface/context creation failed");
        eglSwapInterval(egl_display,0);
    }
    void option(const char *name,const char *value) {
        int result=mpv_set_option_string(core,name,value);
        if(result<0) throw std::runtime_error(std::string("mpv option ")+name+": "+mpv_error_string(result));
    }
    void control_loop() {
        try {
            { std::unique_lock<std::mutex> lock(mutex); cv.wait(lock,[this]{return surface_ready || stopping || state==3;}); }
            if(stopping || state==3) return;
            core=mpv_create(); if(!core) throw std::runtime_error("Cannot create libmpv player");
            option("config","no"); option("load-scripts","no");
            option("access-references","no"); option("load-unsafe-playlists","no");
            if(!source_filter.empty()) option("vf",source_filter.c_str());
            if(color.start_seconds>0) option("start",std::to_string(color.start_seconds).c_str());
            mpv_set_option_string(core,"ytdl","no");
            option("audio","no"); option("sub","no"); mpv_set_option_string(core,"osc","no"); option("osd-level","0");
            option("input-default-bindings","no"); option("input-vo-keyboard","no");
            option("vo","libmpv"); option("hwdec",display && hardware_suitable ? "auto" : "no"); option("hwdec-codecs","all");
            option("loop-file","inf"); option("keep-open","yes"); option("video-unscaled","no"); option("panscan","1");
            option("target-prim",hdr ? "bt.2020" : sdr_target.c_str());
            option("target-trc",hdr ? "pq" : managed ? "gamma2.2" : "srgb");
            if(display) {
                // High-efficiency wallpaper rendering for live playback
                option("scale","bilinear"); option("cscale","bilinear"); option("dscale","bilinear");
                option("correct-downscaling","no");
                option("fbo-format",hdr ? "rgba16f" : (depth==10 ? "rgb10_a2" : "rgba8"));
                option("framedrop","decoder");
                option("display-fps-override",std::to_string(fps_cap.load()).c_str());
            } else {
                option("scale","spline36"); option("cscale","spline36"); option("dscale","mitchell");
                option("fbo-format","rgba16f");
                option("framedrop","vo");
            }
            int ret=mpv_initialize(core); if(ret<0) throw std::runtime_error(mpv_error_string(ret));
            mpv_request_log_messages(core,"warn");
            { std::lock_guard<std::mutex> lock(mutex); core_ready=true; } cv.notify_all();
            { std::unique_lock<std::mutex> lock(mutex); cv.wait(lock,[this]{return renderer_ready || stopping || state==3;}); }
            if(stopping || state==3) return;
            const char *load[]={"loadfile",path.c_str(),nullptr};
            ret=mpv_command(core,load); if(ret<0) throw std::runtime_error(mpv_error_string(ret));
            bool was_paused=false;
            auto next_stats=Clock::now();
            auto next_position=Clock::now();
            int64_t previous_drop=0;
            while(!stopping && state!=3) {
                bool now=paused.load() || throttled.load();
                if(now!=was_paused) { int flag=now; mpv_set_property(core,"pause",MPV_FORMAT_FLAG,&flag); was_paused=now; if(submitted>0) state=now ? 2 : 1; last_present=0; }
                if(Clock::now()>=next_position) {
                    double value=0;
                    if(mpv_get_property(core,"time-pos",MPV_FORMAT_DOUBLE,&value)>=0) position=value;
                    next_position=Clock::now()+std::chrono::milliseconds(100);
                }
                if(Clock::now()>=next_stats) {
                    int64_t drops=0;
                    if(mpv_get_property(core,"frame-drop-count",MPV_FORMAT_INT64,&drops)>=0) {
                        engine_dropped+=drops>=previous_drop ? drops-previous_drop : drops;
                        previous_drop=drops;
                    }
                    next_stats=Clock::now()+std::chrono::seconds(1);
                }
                auto event=mpv_wait_event(core,0.02);
                if(event->event_id==MPV_EVENT_FILE_LOADED && color.start_seconds>0) {
                    // start applies only to this handoff, not subsequent loops.
                    mpv_set_property_string(core,"options/start","none");
                }
                if(event->event_id==MPV_EVENT_VIDEO_RECONFIG) {
                    char *backend=mpv_get_property_string(core,"hwdec-current");
                    if(backend) { std::fprintf(stderr,"[wallpaper-mpv] decoder=%s\n",backend); mpv_free(backend); }
                }
                if(event->event_id==MPV_EVENT_END_FILE) {
                    auto end=static_cast<mpv_event_end_file*>(event->data);
                    if(end->reason==MPV_END_FILE_REASON_ERROR) throw std::runtime_error(mpv_error_string(end->error));
                } else if(event->event_id==MPV_EVENT_LOG_MESSAGE) {
                    auto log=static_cast<mpv_event_log_message*>(event->data);
                    std::fprintf(stderr,"[wallpaper-mpv:%s] %s",log->prefix,log->text);
                }
            }
        } catch(const std::exception &e) { fail(e.what()); }
    }
    static void frame_done(void *data,wl_callback *cb,uint32_t) {
        auto p=static_cast<Player*>(data); wl_callback_destroy(cb); p->frame_callback=nullptr; p->throttled=false; p->cv.notify_all();
    }
    static void feedback_output(void *,struct wp_presentation_feedback *,wl_output *) {}
    static void feedback_presented(void *data,struct wp_presentation_feedback *fb,uint32_t hi,uint32_t lo,uint32_t ns,uint32_t,uint32_t,uint32_t,uint32_t) {
        auto p=static_cast<Player*>(data);
        int64_t us=((int64_t(hi)<<32)|lo)*1000000+ns/1000;
        auto last=p->last_present.exchange(us);
        if(last>0 && us>last) p->max_interval=std::max(p->max_interval.load(),us-last);
        ++p->presented; p->feedbacks.erase(fb); wp_presentation_feedback_destroy(fb);
    }
    static void feedback_discarded(void *data,struct wp_presentation_feedback *fb) {
        auto p=static_cast<Player*>(data); ++p->discarded; p->feedbacks.erase(fb); wp_presentation_feedback_destroy(fb);
    }
    void render_loop() {
        try {
            probe_source_color();
            init_surface();
            { std::lock_guard<std::mutex> lock(mutex); surface_ready=true; } cv.notify_all();
            { std::unique_lock<std::mutex> lock(mutex); cv.wait(lock,[this]{return core_ready || stopping || state==3;}); }
            if(!stopping && state!=3) {
                mpv_opengl_init_params gl={proc,nullptr};
                int advanced=1;
                mpv_render_param init[]={{MPV_RENDER_PARAM_API_TYPE,const_cast<char*>(MPV_RENDER_API_TYPE_OPENGL)},
                    {MPV_RENDER_PARAM_OPENGL_INIT_PARAMS,&gl},{MPV_RENDER_PARAM_ADVANCED_CONTROL,&advanced},
                    {MPV_RENDER_PARAM_WL_DISPLAY,display},{MPV_RENDER_PARAM_INVALID,nullptr}};
                int ret=mpv_render_context_create(&renderer,core,init);
                if(ret<0) throw std::runtime_error(std::string("mpv GPU renderer: ")+mpv_error_string(ret));
                mpv_render_context_set_update_callback(renderer,update,this);
                { std::lock_guard<std::mutex> lock(mutex); renderer_ready=true; } cv.notify_all();
                auto next=Clock::now();
                int old_w=0,old_h=0;
                bool pending=false;
                while(!stopping && state!=3) {
                    if(display && wl_display_dispatch_queue_pending(display,queue)<0) throw std::runtime_error("Wallpaper Wayland connection lost");
                    if(dirty.exchange(false)) pending |= (mpv_render_context_update(renderer)&MPV_RENDER_UPDATE_FRAME)!=0;
                    int w=std::clamp(int(width*scale),1,7680),h=std::clamp(int(height*scale),1,7680);
                    bool resized=w!=old_w || h!=old_h;
                    if(resized && display) { wl_egl_window_resize(egl_window,w,h,0,0); wp_viewport_set_destination(viewport,width,height); }
                    auto now=Clock::now();
                    // Compositors stop frame callbacks when a child is fully
                    // covered. Stop decode too, even before shell occlusion
                    // tracking catches up; the callback resumes it on reveal.
                    if(frame_callback && now-frame_requested>std::chrono::milliseconds(250)) throttled=true;
                    mpv_render_frame_info frame_info{};
                    mpv_render_context_get_info(renderer, {MPV_RENDER_PARAM_NEXT_FRAME_INFO, &frame_info});
                    const auto clock_us=mpv_get_time_us(core);
                    // mpv 0.41's renderer passes internal nanosecond PTS here,
                    // although the public header still describes microseconds.
                    // Normalize against its clock, supporting both API variants.
                    const auto target_us=frame_info.target_time > clock_us*100
                        ? frame_info.target_time/1000 : frame_info.target_time;
                    const bool due = target_us <= clock_us;
                    if(!(frame_info.flags & MPV_RENDER_FRAME_INFO_PRESENT)) pending=false;
                    if(due && pending && now+std::chrono::microseconds(8333) < next && !paused && submitted>0) {
                        // Consume a due source frame without GPU rendering when
                        // it falls between our 60 Hz presentation slots.
                        int skip=1,block=0;
                        mpv_render_param skipped[]={{MPV_RENDER_PARAM_SKIP_RENDERING,&skip},
                            {MPV_RENDER_PARAM_BLOCK_FOR_TARGET_TIME,&block},{MPV_RENDER_PARAM_INVALID,nullptr}};
                        mpv_render_context_render(renderer,skipped);
                        // This source frame was intentionally not submitted,
                        // but the playback engine must still complete its slot.
                        mpv_render_context_report_swap(renderer);
                        pending=false;
                    }
                    if(due && (pending || (resized && submitted>0)) && now>=next && !frame_callback && (!paused || submitted==0 || resized)) {
                        mpv_opengl_fbo fbo={0,w,h,depth==16 ? GL_RGBA16F : depth==10 ? GL_RGB10_A2 : GL_RGBA8};
                        int flip=1,block=0;
                        mpv_render_param params[]={{MPV_RENDER_PARAM_OPENGL_FBO,&fbo},{MPV_RENDER_PARAM_FLIP_Y,&flip},
                            {MPV_RENDER_PARAM_DEPTH,&depth},{MPV_RENDER_PARAM_BLOCK_FOR_TARGET_TIME,&block},{MPV_RENDER_PARAM_INVALID,nullptr}};
                        // No core/control calls or waits for the control worker here.
                        auto render_started=Clock::now();
                        ret=mpv_render_context_render(renderer,params);
                        if(ret<0) throw std::runtime_error(mpv_error_string(ret));
                        if(display) {
                            static const wl_callback_listener fl={frame_done};
                            frame_requested=Clock::now();
                            frame_callback=wl_surface_frame(surface); own_queue(frame_callback); wl_callback_add_listener(frame_callback,&fl,this);
                            if(presentation && feedbacks.size()<3) {
                                static const wp_presentation_feedback_listener pl={feedback_output,feedback_presented,feedback_discarded};
                                auto fb=wp_presentation_feedback(presentation,surface); own_queue(fb);
                                feedbacks.insert(fb); wp_presentation_feedback_add_listener(fb,&pl,this);
                            }
                            auto swap_started=Clock::now();
                            if(!eglSwapBuffers(egl_display,egl_surface)) throw std::runtime_error("Wallpaper EGL swap failed");
                            auto finished=Clock::now();
                            if(std::getenv("DAISYOS_VIDEO_DIAGNOSTICS") && submitted.load()%60==0) std::fprintf(stderr,"[wallpaper-mpv] render=%lldus swap=%lldus target-delay=%lldus\n",
                                (long long)std::chrono::duration_cast<std::chrono::microseconds>(swap_started-render_started).count(),
                                (long long)std::chrono::duration_cast<std::chrono::microseconds>(finished-swap_started).count(),
                                (long long)(mpv_get_time_us(core)-target_us));
                            wl_display_flush(display);
                            mpv_render_context_report_swap(renderer);
                        } else if(pending) {
                            std::vector<unsigned char> pixels(size_t(w)*h*4);
                            glReadPixels(0,0,w,h,GL_BGRA,GL_UNSIGNED_BYTE,pixels.data());
                            { std::lock_guard<std::mutex> lock(mutex); snapshot=std::move(pixels); }
                        }
                        // A resize before the first video update is a black preroll,
                        // not a valid wallpaper frame.
                        ++submitted;
                        if(pending) state=paused ? 2 : 1;
                        cv.notify_all(); pending=false; old_w=w; old_h=h;
                        const int cap=std::clamp(fps_cap.load(),10,60);
                        const auto frame_interval=std::chrono::microseconds(1000000/cap);
                        next+=frame_interval;
                        if(next<=now) next=now+frame_interval;
                        if(!display && submitted>0) paused=true;
                    }
                    auto sleep_dur=throttled ? std::chrono::milliseconds(50)
                        : std::clamp(std::chrono::duration_cast<std::chrono::milliseconds>(next-now),std::chrono::milliseconds(1),std::chrono::milliseconds(16));
                    std::unique_lock<std::mutex> lock(mutex);
                    cv.wait_for(lock,sleep_dur,[this]{return stopping.load() || dirty.load();});
                }
            }
        } catch(const std::exception &e) { fail(e.what()); }
        cleanup();
    }
    void cleanup() {
        if(renderer) { mpv_render_context_set_update_callback(renderer,nullptr,nullptr); mpv_render_context_free(renderer); renderer=nullptr; }
        if(egl_display!=EGL_NO_DISPLAY) {
            eglMakeCurrent(egl_display,EGL_NO_SURFACE,EGL_NO_SURFACE,EGL_NO_CONTEXT);
            if(egl_surface!=EGL_NO_SURFACE) eglDestroySurface(egl_display,egl_surface);
            if(egl_context!=EGL_NO_CONTEXT) eglDestroyContext(egl_display,egl_context);
            // A borrowed display can be shared by Avalonia; do not eglTerminate it.
            if(!display) eglTerminate(egl_display);
        }
        if(egl_window) wl_egl_window_destroy(egl_window);
        if(frame_callback) wl_callback_destroy(frame_callback);
        for(auto fb:feedbacks) wp_presentation_feedback_destroy(fb);
        if(color_surface) wp_color_management_surface_v1_destroy(color_surface);
        if(description) wp_image_description_v1_destroy(description);
        if(viewport) wp_viewport_destroy(viewport);
        if(subsurface) wl_subsurface_destroy(subsurface);
        if(surface) wl_surface_destroy(surface);
        if(colors) wp_color_manager_v1_destroy(colors);
        if(presentation) wp_presentation_destroy(presentation);
        if(viewporter) wp_viewporter_destroy(viewporter);
        if(subcompositor) wl_subcompositor_destroy(subcompositor);
        if(compositor) wl_compositor_destroy(compositor);
        if(registry) wl_registry_destroy(registry);
        if(queue) wl_event_queue_destroy(queue);
        if(display) wl_display_flush(display);
    }
};
}
extern "C" {
void *daisy_mpv_create(void *display,void *parent,const char *path,int width,int height,double scale,const DaisyMpvColor *color) {
    if(!display || !parent || !path || path[0]!='/') return nullptr;
    try { return new Player(display,parent,path,width,height,scale,color); } catch(...) { return nullptr; }
}
void daisy_mpv_resize(void *handle,int width,int height,double scale) {
    if(!handle || width<1 || height<1 || width>7680 || height>7680 || scale<=0 || scale>8) return;
    auto p=static_cast<Player*>(handle); p->width=width; p->height=height; p->scale=scale; p->cv.notify_all();
}
void daisy_mpv_pause(void *handle,int paused) { if(handle) { auto p=static_cast<Player*>(handle); p->paused=paused!=0; p->cv.notify_all(); } }
void daisy_mpv_set_fps_cap(void *handle,int fps) {
    if(handle) {
        auto p=static_cast<Player*>(handle);
        int cap=std::clamp(fps,10,60);
        p->fps_cap=cap;
        if(p->core) {
            std::string s=std::to_string(cap);
            mpv_set_property_string(p->core,"display-fps-override",s.c_str());
        }
        p->cv.notify_all();
    }
}
void daisy_mpv_destroy(void *handle) { delete static_cast<Player*>(handle); }
void daisy_mpv_stats(void *handle,DaisyMpvStats *out) {
    if(!handle || !out) return;
    auto p=static_cast<Player*>(handle); *out={};
    out->state=p->state; out->hdr_active=p->hdr; out->color_managed=p->managed; out->buffer_depth=p->reported_depth;
    out->submitted=p->submitted; out->presented=p->presented; out->discarded=p->discarded;
    out->position_seconds=p->position;
    out->last_present_us=p->last_present; out->max_interval_us=p->max_interval; out->engine_dropped=p->engine_dropped;
    std::lock_guard<std::mutex> lock(p->mutex); std::snprintf(out->message,sizeof(out->message),"%s",p->message.c_str());
}
int daisy_mpv_preview(const char *path,int width,int height,void *bgra,int stride) {
    if(!path || path[0]!='/' || !bgra || width<1 || height<1 || width>1920 || height>1080 || stride<width*4) return -1;
    static std::mutex preview_mutex;
    std::lock_guard<std::mutex> preview_lock(preview_mutex);
    try {
        Player p(nullptr,nullptr,path,width,height,1,nullptr);
        { std::unique_lock<std::mutex> lock(p.mutex); p.cv.wait_for(lock,std::chrono::seconds(8),[&]{return p.submitted>0 || p.state==3;}); }
        std::lock_guard<std::mutex> lock(p.mutex);
        if(p.snapshot.size()!=size_t(width)*height*4) return -1;
        for(int y=0;y<height;y++) std::memcpy(static_cast<unsigned char*>(bgra)+size_t(y)*stride,
            p.snapshot.data()+size_t(height-y-1)*width*4,size_t(width)*4);
        return 0;
    } catch(...) { return -1; }
}
}
