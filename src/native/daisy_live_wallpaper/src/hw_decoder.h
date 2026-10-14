#ifndef DAISY_HW_DECODER_H
#define DAISY_HW_DECODER_H

#include <string>
#include <atomic>
#include <cstdint>

extern "C" {
#include <libavformat/avformat.h>
#include <libavcodec/avcodec.h>
#include <libavutil/hwcontext.h>
}

namespace daisy {

enum class HWBackend {
    Software,
    VAAPI,
    NVDEC,
    QSV,
    Vulkan,
    DRM,
    V4L2M2M
};

struct StatsCollector {
    char decoder_backend[64] = "software";
    char codec[32]           = "unknown";
    int  src_width           = 0;
    int  src_height          = 0;
    double src_fps           = 0.0;
    double displayed_fps     = 0.0;
    std::atomic<int> decoded_frames{0};
    std::atomic<int> dropped_frames{0};
    std::atomic<int> decode_time_us{0};
    std::atomic<int> queue_depth{0};
    int zero_copy            = 0;
    int dmabuf_active        = 0;
    int egl_import_active    = 0;
    int drm_overlay          = 0;
    std::atomic<int> cpu_fallback_count{0};
    std::atomic<int> gpu_copy_fallback{0};
    std::atomic<int> is_playing{0};
    bool audio_opened        = false; // MUST remain false always
};

struct VideoInfo {
    int stream_index = -1;
    int width        = 0;
    int height       = 0;
    double fps       = 0.0;
    AVRational time_base{1, 1000};
    std::string codec_name;
    std::string decoder_name;
    HWBackend backend = HWBackend::Software;
    AVHWDeviceType hw_type = AV_HWDEVICE_TYPE_NONE;
    bool has_closed_gop_at_start = false;
};

class HardwareDecoder {
public:
    HardwareDecoder() = default;
    ~HardwareDecoder();

    HardwareDecoder(const HardwareDecoder &) = delete;
    HardwareDecoder &operator=(const HardwareDecoder &) = delete;

    int open(const std::string &path, StatsCollector &stats);
    AVFrame *decode_next_frame(bool *eof);
    bool retry_software_after_first_frame_failure(StatsCollector &stats);
    int seek_to_start();
    int seek_and_discard_to_target(int64_t target_pts_us);
    void close();

    const VideoInfo &info() const { return info_; }
    AVCodecContext *codec_context() const { return codec_ctx_; }
    AVBufferRef *hw_device_context() const { return hw_device_; }
    AVPixelFormat hw_pixel_format() const { return hw_pix_fmt_; }

private:
    static AVPixelFormat get_hw_format_cb(AVCodecContext *ctx, const AVPixelFormat *pix_fmts);
    int try_open_hw_backend(HWBackend backend, AVHWDeviceType hw_type, const char *decoder_name, StatsCollector &stats);
    int open_software(StatsCollector &stats);

    AVFormatContext *fmt_ctx_ = nullptr;
    AVCodecContext  *codec_ctx_ = nullptr;
    AVBufferRef     *hw_device_ = nullptr;
    AVPixelFormat    hw_pix_fmt_ = AV_PIX_FMT_NONE;
    VideoInfo        info_;
};

} // namespace daisy

#endif // DAISY_HW_DECODER_H
