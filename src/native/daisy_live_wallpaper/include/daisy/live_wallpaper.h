#ifndef DAISY_LIVE_WALLPAPER_H
#define DAISY_LIVE_WALLPAPER_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

#define DAISY_MAX_PLANES 4

typedef void* DaisyWallpaperHandle;

typedef struct DaisyVideoFrame {
    int width;
    int height;
    uint32_t pixel_format;
    uint64_t modifier;

    int fd[DAISY_MAX_PLANES];
    uint32_t stride[DAISY_MAX_PLANES];
    uint32_t offset[DAISY_MAX_PLANES];

    int n_planes;
    int64_t pts_us;
    int acquire_fence;
    int is_hardware;
} DaisyVideoFrame;

typedef struct DaisyWallpaperStats {
    char decoder_backend[64];
    char codec[32];
    int src_width;
    int src_height;
    double src_fps;
    double displayed_fps;
    int decoded_frames;
    int dropped_frames;
    int decode_time_us;
    int queue_depth;
    int zero_copy;
    int dmabuf_active;
    int egl_import_active;
    int drm_overlay;
    int cpu_fallback_count;
    int gpu_copy_fallback;
    int is_playing;
} DaisyWallpaperStats;

DaisyWallpaperHandle daisy_wallpaper_create(void);
int daisy_wallpaper_load(DaisyWallpaperHandle h, const char *path);
void daisy_wallpaper_play(DaisyWallpaperHandle h);
void daisy_wallpaper_pause(DaisyWallpaperHandle h);
void daisy_wallpaper_stop(DaisyWallpaperHandle h);
typedef void (*DaisyFrameCallback)(void);
void daisy_wallpaper_set_frame_callback(DaisyWallpaperHandle h, DaisyFrameCallback callback);

void daisy_wallpaper_destroy(DaisyWallpaperHandle h);
void daisy_wallpaper_set_fps_cap(DaisyWallpaperHandle h, int fps);
// Sets the physical presentation size. Frames are scaled before they cross
// into the shell, avoiding a full-resolution CPU buffer when a 4K/8K source is
// shown on a smaller display.
void daisy_wallpaper_set_output_size(DaisyWallpaperHandle h, int width, int height);
int daisy_wallpaper_try_get_frame(DaisyWallpaperHandle h, DaisyVideoFrame *out);
void daisy_wallpaper_get_stats(DaisyWallpaperHandle h, DaisyWallpaperStats *stats);
void daisy_wallpaper_print_stats(DaisyWallpaperHandle h);

#ifdef __cplusplus
}
#endif

#endif // DAISY_LIVE_WALLPAPER_H
