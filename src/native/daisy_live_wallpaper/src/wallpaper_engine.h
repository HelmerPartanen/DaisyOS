#ifndef DAISY_WALLPAPER_ENGINE_H
#define DAISY_WALLPAPER_ENGINE_H

#include "../include/daisy/live_wallpaper.h"
#include "hw_decoder.h"
#include "dmabuf_exporter.h"

#include <string>
#include <thread>
#include <mutex>
#include <condition_variable>
#include <queue>
#include <atomic>

namespace daisy {

class WallpaperEngine {
public:
    WallpaperEngine();
    ~WallpaperEngine();

    WallpaperEngine(const WallpaperEngine &) = delete;
    WallpaperEngine &operator=(const WallpaperEngine &) = delete;

    int load(const std::string &path);
    void play();
    void pause();
    void stop();
    void set_fps_cap(int fps);
    void set_output_size(int width, int height);

    void set_frame_callback(DaisyFrameCallback callback) { frame_callback_.store(callback); }
    bool try_get_frame(DaisyVideoFrame *out_frame);
    void get_stats(DaisyWallpaperStats *out_stats) const;
    void print_stats() const;

private:
    void decoder_thread_loop();
    void clear_frame_queue();
    static void release_frame(DaisyVideoFrame &frame);

    HardwareDecoder decoder_;
    DmabufExporter  exporter_;
    StatsCollector  stats_;

    std::string path_;
    std::thread decode_thread_;
    std::mutex  queue_mutex_;
    std::condition_variable queue_cv_;

    std::queue<DaisyVideoFrame> frame_queue_;
    std::atomic<DaisyFrameCallback> frame_callback_{nullptr};
    static constexpr size_t kMaxQueueDepth = 3;

    std::atomic<bool> is_running_{false};
    std::atomic<bool> is_paused_{false};
    std::atomic<bool> stop_requested_{false};
    std::atomic<bool> needs_reanchor_{true};
    // Zero means source-timestamp paced (the normal wallpaper path).
    std::atomic<int>  fps_cap_{0};
    std::atomic<int>  output_width_{0};
    std::atomic<int>  output_height_{0};

    int64_t last_pts_us_ = 0;
    bool has_last_presentation_timestamp_ = false;
    std::chrono::steady_clock::time_point last_frame_time_;
    DaisyVideoFrame last_valid_frame_{};
    bool has_last_valid_frame_ = false;
};

} // namespace daisy

#endif // DAISY_WALLPAPER_ENGINE_H
