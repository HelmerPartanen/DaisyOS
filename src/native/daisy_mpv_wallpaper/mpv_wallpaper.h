#pragma once
#include <stdint.h>
#ifdef __cplusplus
extern "C" {
#endif
// The display/parent are borrowed. Destroy the player before either disconnects.
typedef struct DaisyMpvStats {
    int state; // 0 loading, 1 playing, 2 paused, 3 failed
    int hdr_active;
    int color_managed;
    int buffer_depth;
    int64_t submitted;
    int64_t presented;
    int64_t discarded;
    int64_t last_present_us;
    int64_t max_interval_us;
    int64_t engine_dropped;
    double position_seconds;
    char message[256];
} DaisyMpvStats;
typedef struct DaisyMpvColor {
    int hdr; // 0 SDR, 1 PQ, 2 HLG
    int peak_nits;
    int max_cll;
    int max_fall;
    // mastering primaries rx,ry,gx,gy,bx,by,wx,wy (millionths)
    int primaries[8];
    int min_luminance; // ten-thousandths of a nit
    double start_seconds;
} DaisyMpvColor;
void *daisy_mpv_create(void *display, void *parent, const char *path,
                     int width, int height, double scale, const DaisyMpvColor *color);
void daisy_mpv_resize(void *handle, int width, int height, double scale);
void daisy_mpv_pause(void *handle, int paused);
void daisy_mpv_stats(void *handle, DaisyMpvStats *out);
void daisy_mpv_destroy(void *handle);
// One GPU-rendered SDR snapshot; preview readback is never used for playback.
int daisy_mpv_preview(const char *path, int width, int height, void *bgra, int stride);
#ifdef __cplusplus
}
#endif
