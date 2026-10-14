/// Public C API — thin wrappers over WallpaperEngine.
#include "wallpaper_engine.h"
#include "../include/daisy/live_wallpaper.h"

using namespace daisy;

extern "C" {

DaisyWallpaperHandle daisy_wallpaper_create(void) {
    try { return reinterpret_cast<DaisyWallpaperHandle>(new WallpaperEngine()); }
    catch (...) { return nullptr; }
}

int daisy_wallpaper_load(DaisyWallpaperHandle h, const char *path) {
    if (!h || !path) return -1;
    return reinterpret_cast<WallpaperEngine *>(h)->load(path);
}

void daisy_wallpaper_play(DaisyWallpaperHandle h) {
    if (h) reinterpret_cast<WallpaperEngine *>(h)->play();
}

void daisy_wallpaper_pause(DaisyWallpaperHandle h) {
    if (h) reinterpret_cast<WallpaperEngine *>(h)->pause();
}

void daisy_wallpaper_stop(DaisyWallpaperHandle h) {
    if (h) reinterpret_cast<WallpaperEngine *>(h)->stop();
}

void daisy_wallpaper_set_fps_cap(DaisyWallpaperHandle h, int fps) {
    if (h) reinterpret_cast<WallpaperEngine *>(h)->set_fps_cap(fps);
}

void daisy_wallpaper_set_output_size(DaisyWallpaperHandle h, int width, int height) {
    if (h) reinterpret_cast<WallpaperEngine *>(h)->set_output_size(width, height);
}

int daisy_wallpaper_try_get_frame(DaisyWallpaperHandle h, DaisyVideoFrame *out) {
    if (!h || !out) return 0;
    return reinterpret_cast<WallpaperEngine *>(h)->try_get_frame(out) ? 1 : 0;
}

void daisy_wallpaper_set_frame_callback(DaisyWallpaperHandle h, DaisyFrameCallback callback) {
    if (h) reinterpret_cast<WallpaperEngine *>(h)->set_frame_callback(callback);
}

void daisy_wallpaper_get_stats(DaisyWallpaperHandle h, DaisyWallpaperStats *stats) {
    if (!h || !stats) return;
    reinterpret_cast<WallpaperEngine *>(h)->get_stats(stats);
}

void daisy_wallpaper_print_stats(DaisyWallpaperHandle h) {
    if (h) reinterpret_cast<WallpaperEngine *>(h)->print_stats();
}

void daisy_wallpaper_destroy(DaisyWallpaperHandle h) {
    if (h) delete reinterpret_cast<WallpaperEngine *>(h);
}

} // extern "C"
