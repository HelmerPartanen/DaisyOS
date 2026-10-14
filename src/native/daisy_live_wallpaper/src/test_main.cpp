/// Standalone test for the daisy_live_wallpaper native library.
/// Usage: daisy_wallpaper_test <video_file> [frame_count]
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <ctime>
#include <atomic>
#include <algorithm>
#include <chrono>
#include <thread>
static std::atomic<int> notifications{0};
static void frame_available() { ++notifications; }
#include <unistd.h>
#include "../include/daisy/live_wallpaper.h"

int main(int argc, char *argv[]) {
    if (argc < 2) {
        std::fprintf(stderr, "Usage: %s <video_file> [frame_count]\n", argv[0]);
        return 1;
    }
    const char *path  = argv[1];
    int max_frames    = (argc >= 3) ? std::atoi(argv[2]) : 60;
    if (max_frames <= 0) max_frames = 60;

    std::fprintf(stderr, "[test] Opening: %s (want %d frames)\n", path, max_frames);

    DaisyWallpaperHandle h = daisy_wallpaper_create();
    if (!h) { std::fprintf(stderr, "[test] create failed\n"); return 1; }

    int ret = daisy_wallpaper_load(h, path);
    if (ret != 0) {
        std::fprintf(stderr, "[test] load failed: %d\n", ret);
        daisy_wallpaper_destroy(h);
        return 1;
    }

    daisy_wallpaper_set_frame_callback(h, frame_available);
    daisy_wallpaper_set_fps_cap(h, 60);
    daisy_wallpaper_play(h);

    // A stalled consumer must retain one latest frame rather than a backlog.
    const auto started = std::chrono::steady_clock::now();
    std::this_thread::sleep_for(std::chrono::milliseconds(350));
    DaisyWallpaperStats initial_stats{};
    daisy_wallpaper_get_stats(h, &initial_stats);
    if (initial_stats.queue_depth > 1 || notifications.load() == 0) {
        daisy_wallpaper_destroy(h); return 2;
    }
    const double fps = initial_stats.src_fps > 0 ? std::min(initial_stats.src_fps, 60.0) : 30.0;
    if (notifications.load() > static_cast<int>(fps * 0.35) + 3) {
        std::fprintf(stderr, "[test] Decoder ran faster than the source clock\n");
        daisy_wallpaper_destroy(h); return 4;
    }
    daisy_wallpaper_pause(h);
    std::this_thread::sleep_for(std::chrono::milliseconds(30));
    const int paused_count = notifications.load();
    std::this_thread::sleep_for(std::chrono::milliseconds(100));
    if (notifications.load() != paused_count) {
        daisy_wallpaper_destroy(h); return 5;
    }
    daisy_wallpaper_play(h);
    int received = 0, polls = 0;
    const int max_polls = static_cast<int>((max_frames / fps + 3.0) * 500);

    while (received < max_frames && polls < max_polls) {
        DaisyVideoFrame frame{};
        for (int i = 0; i < DAISY_MAX_PLANES; ++i) frame.fd[i] = -1;
        frame.acquire_fence = -1;

        if (daisy_wallpaper_try_get_frame(h, &frame)) {
            std::fprintf(stdout,
                "Frame %3d: pts=%7lld us  %dx%d  fourcc=0x%08x  hw=%d  "
                "planes=%d  fds=[%d,%d,%d,%d]  strides=[%u,%u,%u,%u]\n",
                received, (long long)frame.pts_us,
                frame.width, frame.height, frame.pixel_format,
                frame.is_hardware, frame.n_planes,
                frame.fd[0], frame.fd[1], frame.fd[2], frame.fd[3],
                frame.stride[0], frame.stride[1], frame.stride[2], frame.stride[3]);

            // Close DMA-BUF FDs
            for (int i = 0; i < DAISY_MAX_PLANES; ++i) {
                if (frame.fd[i] >= 0) { ::close(frame.fd[i]); frame.fd[i] = -1; }
            }
            if (frame.acquire_fence >= 0) { ::close(frame.acquire_fence); frame.acquire_fence = -1; }

            // Free CPU buffer (software path sentinel: fd[0]==-2)
            if (frame.fd[0] == -2) {
                uintptr_t ptr = (uintptr_t)frame.stride[0] | ((uintptr_t)frame.stride[1] << 32);
                if (ptr) std::free(reinterpret_cast<void *>(ptr));
            }
            ++received;
            // Visibility updates may redundantly request Play on every frame.
            // That must not reset the presentation clock.
            daisy_wallpaper_play(h);
        } else {
            struct timespec ts{0, 2000000}; // 2ms
            nanosleep(&ts, nullptr);
        }
        ++polls;
    }

    const double elapsed = std::chrono::duration<double>(std::chrono::steady_clock::now() - started).count();
    const bool paced = elapsed >= (max_frames - 2) / fps && elapsed < max_frames / fps + 3.0;
    daisy_wallpaper_stop(h);
    daisy_wallpaper_print_stats(h);
    daisy_wallpaper_destroy(h);
    const int stopped_count = notifications.load();
    std::this_thread::sleep_for(std::chrono::milliseconds(50));
    if (notifications.load() != stopped_count) return 3;

    std::fprintf(stderr, "[test] Done: %d frames in %d polls\n", received, polls);
    return received == max_frames && paced ? 0 : 1;
}
