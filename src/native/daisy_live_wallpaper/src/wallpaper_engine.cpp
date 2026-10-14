#include "wallpaper_engine.h"
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <chrono>
#include <algorithm>
#include <cmath>
#include <unistd.h>
#include <drm_fourcc.h>

extern "C" {
#include <libavutil/mathematics.h>
}

namespace daisy {

WallpaperEngine::WallpaperEngine() = default;

WallpaperEngine::~WallpaperEngine() {
    stop();
}

int WallpaperEngine::load(const std::string &path) {
    stop();
    last_pts_us_ = 0;
    has_last_presentation_timestamp_ = false;
    needs_reanchor_ = true;

    path_ = path;
    int ret = decoder_.open(path_, stats_);
    if (ret < 0) {
        std::fprintf(stderr, "[daisy-engine] Failed to open decoder for %s: %d\n", path.c_str(), ret);
        return ret;
    }

    exporter_.init(decoder_.info().hw_type, stats_);
    stats_.is_playing = 0;
    return 0;
}

void WallpaperEngine::play() {
    if (is_running_) {
        if (is_paused_.load()) needs_reanchor_ = true;
        is_paused_ = false;
        stats_.is_playing = 1;
        queue_cv_.notify_all();
        return;
    }

    needs_reanchor_ = true;
    stop_requested_ = false;
    is_paused_      = false;
    is_running_     = true;
    stats_.is_playing = 1;

    decode_thread_ = std::thread(&WallpaperEngine::decoder_thread_loop, this);
}

void WallpaperEngine::pause() {
    is_paused_        = true;
    stats_.is_playing = 0;
    clear_frame_queue();
    needs_reanchor_   = true;
    queue_cv_.notify_all();
}

void WallpaperEngine::stop() {
    stop_requested_ = true;
    is_paused_      = false;
    queue_cv_.notify_all();

    if (decode_thread_.joinable()) {
        decode_thread_.join();
    }

    is_running_       = false;
    stats_.is_playing = 0;
    clear_frame_queue();
    decoder_.close();
}

void WallpaperEngine::set_fps_cap(int fps) {
    // A cap is only for non-interactive probes/previews. Live wallpaper uses
    // zero and follows the stream timestamps exactly.
    fps_cap_ = fps <= 0 ? 0 : std::clamp(fps, 1, 240);
}

void WallpaperEngine::set_output_size(int width, int height) {
    // Keep the native allocator bounded even if a malformed client asks for an
    // extreme render target. Source validation happens in HardwareDecoder.
    constexpr int kMaxPresentationDimension = 7680;
    output_width_ = std::clamp(width, 0, kMaxPresentationDimension);
    output_height_ = std::clamp(height, 0, kMaxPresentationDimension);
}

void WallpaperEngine::release_frame(DaisyVideoFrame &frame) {
    for (int i = 0; i < DAISY_MAX_PLANES; ++i) {
        if (frame.fd[i] >= 0) ::close(frame.fd[i]);
    }
    if (frame.acquire_fence >= 0) ::close(frame.acquire_fence);
    if (frame.fd[0] == -2) {
        uintptr_t ptr = static_cast<uintptr_t>(frame.stride[0]) |
                        (static_cast<uintptr_t>(frame.stride[1]) << 32);
        if (ptr) std::free(reinterpret_cast<void *>(ptr));
    }
    frame = {};
    for (int i = 0; i < DAISY_MAX_PLANES; ++i) frame.fd[i] = -1;
    frame.acquire_fence = -1;
}

void WallpaperEngine::clear_frame_queue() {
    std::lock_guard<std::mutex> lock(queue_mutex_);
    while (!frame_queue_.empty()) {
        DaisyVideoFrame f = frame_queue_.front();
        frame_queue_.pop();
        release_frame(f);
    }
    stats_.queue_depth = 0;
}

void WallpaperEngine::decoder_thread_loop() {
    int consecutive_export_failures = 0;
    int consecutive_first_frame_decode_failures = 0;
    bool first_frame_software_retry_attempted = false;
    int64_t current_target_pts_us = 0;
    bool needs_anchor = true;
    std::chrono::steady_clock::time_point clock_anchor;
    int64_t pts_anchor_us = 0;
    int64_t fallback_pts_us = 0;
    auto loop_deadline = std::chrono::steady_clock::now();

    while (!stop_requested_) {
        {
            std::unique_lock<std::mutex> lock(queue_mutex_);
            queue_cv_.wait(lock, [this] {
                return stop_requested_ || !is_paused_;
            });
        }

        if (stop_requested_) {
            break;
        }

        if (is_paused_) {
            continue;
        }

        if (needs_reanchor_.exchange(false)) {
            needs_anchor = true;
        }

        bool eof = false;
        AVFrame *raw_frame = decoder_.decode_next_frame(&eof);

        if (eof) {
            // Hold the last frame for its duration before restarting the clock.
            {
                std::unique_lock<std::mutex> lock(queue_mutex_);
                queue_cv_.wait_until(lock, loop_deadline, [this] {
                    return stop_requested_.load() || is_paused_.load();
                });
            }
            // Seek without dropping the currently displayed buffer.
            if (decoder_.seek_to_start() < 0) {
                std::this_thread::sleep_for(std::chrono::milliseconds(20));
            }
            needs_anchor = true;
            continue;
        }

        if (!raw_frame) {
            if (stats_.decoded_frames.load(std::memory_order_relaxed) == 0 &&
                ++consecutive_first_frame_decode_failures >= 24 &&
                !first_frame_software_retry_attempted) {
                first_frame_software_retry_attempted = true;
                if (decoder_.retry_software_after_first_frame_failure(stats_)) {
                    consecutive_first_frame_decode_failures = 0;
                    needs_anchor = true;
                }
            }
            std::this_thread::sleep_for(std::chrono::milliseconds(5));
            continue;
        }
        consecutive_first_frame_decode_failures = 0;

        const int64_t raw_pts = raw_frame->best_effort_timestamp != AV_NOPTS_VALUE
            ? raw_frame->best_effort_timestamp
            : raw_frame->pts;
        const bool has_presentation_timestamp = raw_pts != AV_NOPTS_VALUE;
        const int64_t presentation_pts_us = has_presentation_timestamp
            ? av_rescale_q(raw_pts, decoder_.info().time_base, AVRational{1, 1000000})
            : fallback_pts_us;

        int cap = fps_cap_.load();
        if (cap <= 0) cap = 60; // Default presentation cap: 60 FPS
        double stream_fps = stats_.src_fps;
        double target_fps = (stream_fps > 0.0 && stream_fps < cap) ? stream_fps : static_cast<double>(cap);
        if (target_fps < 1.0) target_fps = 30.0;
        const int64_t target_interval_us = static_cast<int64_t>(1'000'000.0 / target_fps);

        if (needs_anchor || presentation_pts_us < current_target_pts_us - 1'000'000 || presentation_pts_us > current_target_pts_us + 1'000'000) {
            current_target_pts_us = presentation_pts_us;
            clock_anchor = std::chrono::steady_clock::now();
            pts_anchor_us = presentation_pts_us;
            needs_anchor = false;
        }

        // High-framerate frame skipping:
        // Free intermediate frames immediately if their PTS is earlier than our next scheduled slot.
        // This avoids PCIe transfer, color conversion, and memory allocations for unpresented frames.
        if (has_presentation_timestamp && presentation_pts_us < current_target_pts_us) {
            av_frame_free(&raw_frame);
            continue;
        }

        // Pace against the stream clock before transfer/conversion. Keep only
        // the latest due frame when the UI stalls, without decoding entire loops
        // at full speed. Pause/stop interrupt the wait immediately.
        const auto deadline = clock_anchor + std::chrono::microseconds(presentation_pts_us - pts_anchor_us);
        {
            std::unique_lock<std::mutex> lock(queue_mutex_);
            queue_cv_.wait_until(lock, deadline, [this] {
                return stop_requested_.load() || is_paused_.load() || needs_reanchor_.load();
            });
        }
        if (stop_requested_ || is_paused_ || needs_reanchor_) {
            av_frame_free(&raw_frame);
            continue;
        }
        fallback_pts_us = presentation_pts_us + target_interval_us;
        loop_deadline = deadline + std::chrono::microseconds(target_interval_us);
        // Catch up after a slow transfer without doing expensive work for frames
        // already more than one presentation interval late.
        if (std::chrono::steady_clock::now() > deadline + std::chrono::microseconds(target_interval_us)) {
            av_frame_free(&raw_frame);
            stats_.dropped_frames.fetch_add(1, std::memory_order_relaxed);
            continue;
        }

        DaisyVideoFrame frame{};
        exporter_.set_output_size(output_width_.load(), output_height_.load());
        bool exported = exporter_.export_frame(raw_frame, &frame, stats_);
        av_frame_free(&raw_frame);

        if (!exported) {
            if (++consecutive_export_failures >= 30) {
                std::fprintf(stderr, "[daisy-video] Frame transfer failed repeatedly; pausing video wallpaper.\n");
                is_paused_ = true;
                stats_.is_playing = 0;
                consecutive_export_failures = 0;
            }
            std::this_thread::sleep_for(std::chrono::milliseconds(20));
            continue;
        }

        consecutive_export_failures = 0;
        frame.pts_us = presentation_pts_us;

        if (has_presentation_timestamp) {
            if (presentation_pts_us > current_target_pts_us + 500'000) {
                current_target_pts_us = presentation_pts_us + target_interval_us;
            } else {
                current_target_pts_us += target_interval_us;
            }
        }

        {
            std::unique_lock<std::mutex> lock(queue_mutex_);
            if (stop_requested_ || is_paused_ || needs_reanchor_) {
                release_frame(frame);
                continue;
            }
            while (!frame_queue_.empty()) {
                auto stale = frame_queue_.front(); frame_queue_.pop(); release_frame(stale);
                stats_.dropped_frames.fetch_add(1, std::memory_order_relaxed);
            }
            frame_queue_.push(frame);
            stats_.queue_depth = static_cast<int>(frame_queue_.size());
            stats_.decoded_frames.fetch_add(1, std::memory_order_relaxed);
        }
        if (auto callback = frame_callback_.load()) callback();
    }
}

bool WallpaperEngine::try_get_frame(DaisyVideoFrame *out) {
    if (!out) return false;

    std::lock_guard<std::mutex> lock(queue_mutex_);
    if (frame_queue_.empty()) return false;

    *out = frame_queue_.front();
    frame_queue_.pop();
    stats_.queue_depth = static_cast<int>(frame_queue_.size());
    queue_cv_.notify_one();
    return true;
}

void WallpaperEngine::get_stats(DaisyWallpaperStats *out) const {
    if (!out) return;

    std::snprintf(out->decoder_backend, sizeof(out->decoder_backend), "%s", stats_.decoder_backend);
    std::snprintf(out->codec, sizeof(out->codec), "%s", stats_.codec);
    out->src_width          = stats_.src_width;
    out->src_height         = stats_.src_height;
    out->src_fps            = stats_.src_fps;
    out->displayed_fps      = stats_.src_fps;
    out->decoded_frames     = stats_.decoded_frames.load(std::memory_order_relaxed);
    out->dropped_frames     = stats_.dropped_frames.load(std::memory_order_relaxed);
    out->decode_time_us     = stats_.decode_time_us.load(std::memory_order_relaxed);
    out->queue_depth        = stats_.queue_depth;
    out->zero_copy          = stats_.zero_copy;
    out->dmabuf_active      = stats_.dmabuf_active;
    out->egl_import_active  = stats_.egl_import_active;
    out->drm_overlay        = stats_.drm_overlay;
    out->cpu_fallback_count = stats_.cpu_fallback_count.load(std::memory_order_relaxed);
    out->gpu_copy_fallback  = stats_.gpu_copy_fallback.load(std::memory_order_relaxed);
    out->is_playing         = stats_.is_playing;
}

void WallpaperEngine::print_stats() const {
    DaisyWallpaperStats s{};
    get_stats(&s);

    const char *format_str = (s.zero_copy && s.dmabuf_active) ? "NV12" : "BGRA8888";
    const char *topology = (s.zero_copy && s.dmabuf_active) ? exporter_.topology_string() : "CPU presentation";

    std::fprintf(stderr,
        "\nLiveWallpaper:\n"
        "  codec: %s\n"
        "  resolution: %dx%d\n"
        "  source fps: %.2f\n"
        "  decoder: %s\n"
        "  gpu-device-topology: %s\n"
        "  zero-copy: %s\n"
        "  format: %s\n"
        "  dmabuf: %s\n"
        "  sync-mode: %s\n"
        "  presentation: %s\n"
        "  queued frames: %d\n"
        "  dropped frames: %d\n"
        "  audio: not opened\n\n",
        s.codec,
        s.src_width, s.src_height,
        s.src_fps,
        s.decoder_backend,
        topology,
        s.zero_copy ? "yes" : "no",
        format_str,
        s.dmabuf_active ? "yes" : "no",
        exporter_.sync_mode_string(),
        s.drm_overlay ? "DRM overlay" : "Vulkan compositor",
        s.queue_depth,
        s.dropped_frames
    );
}

} // namespace daisy
