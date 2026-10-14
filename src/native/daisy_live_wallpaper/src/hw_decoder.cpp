#include "hw_decoder.h"
#include <cstdio>
#include <cstring>
#include <chrono>

extern "C" {
#include <libavformat/avformat.h>
#include <libavcodec/avcodec.h>
#include <libavutil/hwcontext.h>
#include <libavutil/opt.h>
#include <libavutil/error.h>
#include <libavutil/pixdesc.h>
}

namespace daisy {

namespace {

struct HWTypePreference {
    AVHWDeviceType hw_type;
    const char    *name;
};

constexpr HWTypePreference kHWTypes[] = {
    { AV_HWDEVICE_TYPE_CUDA,   "cuda"   },
    { AV_HWDEVICE_TYPE_VAAPI,  "vaapi"  },
    { AV_HWDEVICE_TYPE_QSV,    "qsv"    },
    { AV_HWDEVICE_TYPE_VULKAN, "vulkan" },
    { AV_HWDEVICE_TYPE_DRM,    "drm"    },
};

thread_local AVPixelFormat g_hw_pix_fmt = AV_PIX_FMT_NONE;

} // anonymous namespace

AVPixelFormat HardwareDecoder::get_hw_format_cb(AVCodecContext *ctx,
                                                  const AVPixelFormat *pix_fmts) {
    for (const AVPixelFormat *p = pix_fmts; *p != AV_PIX_FMT_NONE; ++p) {
        if (*p == g_hw_pix_fmt) return *p;
    }

    if (ctx && ctx->hw_device_ctx) {
        AVHWDeviceContext *device_ctx = reinterpret_cast<AVHWDeviceContext *>(ctx->hw_device_ctx->data);
        for (const AVPixelFormat *p = pix_fmts; *p != AV_PIX_FMT_NONE; ++p) {
            if (device_ctx->type == AV_HWDEVICE_TYPE_VAAPI && *p == AV_PIX_FMT_VAAPI) return *p;
            if (device_ctx->type == AV_HWDEVICE_TYPE_CUDA  && *p == AV_PIX_FMT_CUDA)  return *p;
            if (device_ctx->type == AV_HWDEVICE_TYPE_DRM   && *p == AV_PIX_FMT_DRM_PRIME) return *p;
        }
    }

    return AV_PIX_FMT_NONE;
}

HardwareDecoder::~HardwareDecoder() { close(); }

void HardwareDecoder::close() {
    if (codec_ctx_) { avcodec_free_context(&codec_ctx_); codec_ctx_ = nullptr; }
    if (hw_device_) { av_buffer_unref(&hw_device_);      hw_device_ = nullptr; }
    if (fmt_ctx_)   { avformat_close_input(&fmt_ctx_);   fmt_ctx_   = nullptr; }
    info_      = {};
    hw_pix_fmt_ = AV_PIX_FMT_NONE;
}

int HardwareDecoder::open(const std::string &path, StatsCollector &stats) {
    close();

    int ret = avformat_open_input(&fmt_ctx_, path.c_str(), nullptr, nullptr);
    if (ret < 0) {
        char err[256]; av_strerror(ret, err, sizeof(err));
        std::fprintf(stderr, "[daisy-hw] avformat_open_input failed (%s): %s\n", path.c_str(), err);
        return ret;
    }

    ret = avformat_find_stream_info(fmt_ctx_, nullptr);
    if (ret < 0) {
        std::fprintf(stderr, "[daisy-hw] find_stream_info failed\n");
        close();
        return ret;
    }

    int video_idx = av_find_best_stream(fmt_ctx_, AVMEDIA_TYPE_VIDEO, -1, -1, nullptr, 0);
    if (video_idx < 0) {
        std::fprintf(stderr, "[daisy-hw] No video stream found in %s\n", path.c_str());
        close();
        return video_idx;
    }
    info_.stream_index = video_idx;

    // AUDIO EXCLUSION DIRECTIVE (§1):
    // Discard all non-video streams at demuxer level.
    // Audio stream is NEVER opened, decoded, or demuxed into a decoder context.
    for (unsigned int i = 0; i < fmt_ctx_->nb_streams; ++i) {
        if (static_cast<int>(i) != video_idx) {
            fmt_ctx_->streams[i]->discard = AVDISCARD_ALL;
        }
    }
    stats.audio_opened = false;

    AVStream *stream = fmt_ctx_->streams[video_idx];
    info_.width  = stream->codecpar->width;
    info_.height = stream->codecpar->height;
    info_.time_base = stream->time_base;
    AVRational fr = av_guess_frame_rate(fmt_ctx_, stream, nullptr);
    info_.fps = fr.den > 0 ? (double)fr.num / fr.den : 30.0;

    // Security bounds check (§16)
    if (info_.width <= 0 || info_.width > 8192 || info_.height <= 0 || info_.height > 8192 || info_.fps > 240.0) {
        std::fprintf(stderr, "[daisy-hw] Video dimensions (%dx%d @ %.1f fps) exceed sane bounds\n",
                     info_.width, info_.height, info_.fps);
        close();
        return AVERROR(EINVAL);
    }

    stats.src_width  = info_.width;
    stats.src_height = info_.height;
    stats.src_fps    = info_.fps;

    const AVCodecDescriptor *desc = avcodec_descriptor_get(stream->codecpar->codec_id);
    if (desc) {
        std::snprintf(stats.codec, sizeof(stats.codec), "%s", desc->name ? desc->name : "unknown");
        info_.codec_name = desc->name ? desc->name : "unknown";
    }

    // Try hardware decoding with standard decoder & HW device contexts
    const AVCodec *std_codec = avcodec_find_decoder(stream->codecpar->codec_id);
    if (std_codec) {
        for (const auto &hw_spec : kHWTypes) {
            ret = try_open_hw_backend(HWBackend::VAAPI, hw_spec.hw_type, hw_spec.name, stats);
            if (ret == 0) {
                info_.hw_type      = hw_spec.hw_type;
                info_.decoder_name = hw_spec.name;
                std::fprintf(stderr, "[daisy-hw] Selected HW decoder: %s (%s) for %dx%d @ %.2f FPS\n",
                             hw_spec.name, info_.codec_name.c_str(), info_.width, info_.height, info_.fps);
                // Avalonia currently presents CPU-backed BGRA frames. Hardware
                // decode is still used, but advertising zero-copy here would be
                // inaccurate until a real DMA-BUF importer is connected.
                stats.zero_copy     = 0;
                stats.dmabuf_active = 0;
                return 0;
            }
        }
    }

    // Fallback to software decode
    std::fprintf(stderr, "[daisy-hw] HW decode unavailable for %s — falling back to software decode\n",
                 info_.codec_name.c_str());
    stats.cpu_fallback_count.fetch_add(1, std::memory_order_relaxed);
    ret = open_software(stats);
    if (ret == 0) {
        info_.backend       = HWBackend::Software;
        info_.decoder_name = "software";
        stats.zero_copy     = 0;
        stats.dmabuf_active = 0;
    }
    return ret;
}

int HardwareDecoder::try_open_hw_backend(HWBackend backend,
                                          AVHWDeviceType hw_type,
                                          const char *decoder_name,
                                          StatsCollector &stats) {
    AVStream *stream = fmt_ctx_->streams[info_.stream_index];
    const AVCodec *codec = avcodec_find_decoder(stream->codecpar->codec_id);
    if (!codec) return AVERROR_DECODER_NOT_FOUND;

    AVPixelFormat hw_fmt = AV_PIX_FMT_NONE;
    for (int i = 0; ; ++i) {
        const AVCodecHWConfig *cfg = avcodec_get_hw_config(codec, i);
        if (!cfg) break;
        if ((cfg->methods & AV_CODEC_HW_CONFIG_METHOD_HW_DEVICE_CTX) && cfg->device_type == hw_type) {
            hw_fmt = cfg->pix_fmt;
            break;
        }
    }
    if (hw_fmt == AV_PIX_FMT_NONE) return AVERROR(ENODEV);

    AVBufferRef *hw_dev = nullptr;
    int ret = av_hwdevice_ctx_create(&hw_dev, hw_type, nullptr, nullptr, 0);
    if (ret < 0) return ret;

    AVCodecContext *cc = avcodec_alloc_context3(codec);
    if (!cc) { av_buffer_unref(&hw_dev); return AVERROR(ENOMEM); }

    ret = avcodec_parameters_to_context(cc, stream->codecpar);
    if (ret < 0) { avcodec_free_context(&cc); av_buffer_unref(&hw_dev); return ret; }

    cc->hw_device_ctx = av_buffer_ref(hw_dev);
    g_hw_pix_fmt      = hw_fmt;
    hw_pix_fmt_       = hw_fmt;
    cc->get_format    = get_hw_format_cb;
    cc->thread_count  = 1;
    if (info_.fps > 90.0) {
        cc->skip_frame = AVDISCARD_NONREF;
    }

    ret = avcodec_open2(cc, codec, nullptr);
    if (ret < 0) { avcodec_free_context(&cc); av_buffer_unref(&hw_dev); return ret; }

    if (codec_ctx_) avcodec_free_context(&codec_ctx_);
    if (hw_device_) av_buffer_unref(&hw_device_);
    codec_ctx_ = cc;
    hw_device_ = hw_dev;
    info_.backend = backend;
    std::snprintf(stats.decoder_backend, sizeof(stats.decoder_backend), "%s", decoder_name);
    return 0;
}

bool HardwareDecoder::retry_software_after_first_frame_failure(StatsCollector &stats) {
    if (!fmt_ctx_ || info_.backend == HWBackend::Software) return false;

    const std::string failed_backend = info_.decoder_name;
    if (codec_ctx_) avcodec_free_context(&codec_ctx_);
    if (hw_device_) av_buffer_unref(&hw_device_);
    hw_pix_fmt_ = AV_PIX_FMT_NONE;
    if (avformat_seek_file(fmt_ctx_, info_.stream_index, 0, 0, 0, AVSEEK_FLAG_BACKWARD) < 0) {
        std::fprintf(stderr, "[daisy-hw] first-frame fallback seek failed: codec=%s backend=%s\n",
                     info_.codec_name.c_str(), failed_backend.c_str());
        return false;
    }

    const int ret = open_software(stats);
    if (ret < 0) {
        char error[256]; av_strerror(ret, error, sizeof(error));
        std::fprintf(stderr, "[daisy-hw] first-frame software fallback failed: codec=%s backend=%s reason=%s\n",
                     info_.codec_name.c_str(), failed_backend.c_str(), error);
        return false;
    }

    info_.backend = HWBackend::Software;
    info_.hw_type = AV_HWDEVICE_TYPE_NONE;
    info_.decoder_name = "software";
    stats.cpu_fallback_count.fetch_add(1, std::memory_order_relaxed);
    std::fprintf(stderr, "[daisy-hw] first-frame decode failed: codec=%s backend=%s; retrying once with software\n",
                 info_.codec_name.c_str(), failed_backend.c_str());
    return true;
}

int HardwareDecoder::open_software(StatsCollector &stats) {
    AVStream *stream = fmt_ctx_->streams[info_.stream_index];
    const AVCodec *codec = avcodec_find_decoder(stream->codecpar->codec_id);
    if (!codec) return AVERROR_DECODER_NOT_FOUND;

    AVCodecContext *cc = avcodec_alloc_context3(codec);
    if (!cc) return AVERROR(ENOMEM);

    int ret = avcodec_parameters_to_context(cc, stream->codecpar);
    if (ret < 0) { avcodec_free_context(&cc); return ret; }

    cc->thread_count = 0; // Automatic multi-threading for software decode
    if (info_.fps > 90.0) {
        cc->skip_frame = AVDISCARD_NONREF;
    }
    ret = avcodec_open2(cc, codec, nullptr);
    if (ret < 0) { avcodec_free_context(&cc); return ret; }

    if (codec_ctx_) avcodec_free_context(&codec_ctx_);
    codec_ctx_ = cc;
    std::snprintf(stats.decoder_backend, sizeof(stats.decoder_backend), "software");
    return 0;
}

AVFrame *HardwareDecoder::decode_next_frame(bool *eof) {
    *eof = false;
    if (!fmt_ctx_ || !codec_ctx_) return nullptr;

    AVPacket *pkt   = av_packet_alloc();
    AVFrame  *frame = av_frame_alloc();
    if (!pkt || !frame) { av_packet_free(&pkt); av_frame_free(&frame); return nullptr; }

    int  ret       = 0;
    bool got_frame = false;

    while (!got_frame) {
        ret = av_read_frame(fmt_ctx_, pkt);
        if (ret == AVERROR_EOF) {
            avcodec_send_packet(codec_ctx_, nullptr);
        } else if (ret < 0) {
            break;
        } else if (pkt->stream_index != info_.stream_index) {
            av_packet_unref(pkt);
            continue;
        } else {
            ret = avcodec_send_packet(codec_ctx_, pkt);
            av_packet_unref(pkt);
            if (ret < 0 && ret != AVERROR(EAGAIN)) break;
        }

        ret = avcodec_receive_frame(codec_ctx_, frame);
        if (ret == AVERROR(EAGAIN)) continue;
        if (ret == AVERROR_EOF) { *eof = true; break; }
        if (ret < 0) break;
        got_frame = true;
    }

    av_packet_free(&pkt);
    if (!got_frame) { av_frame_free(&frame); return nullptr; }
    return frame;
}

int HardwareDecoder::seek_to_start() {
    if (!fmt_ctx_) return AVERROR(EINVAL);
    int ret = avformat_seek_file(fmt_ctx_, info_.stream_index, 0, 0, 0, AVSEEK_FLAG_BACKWARD);
    if (ret < 0) std::fprintf(stderr, "[daisy-hw] seek_to_start failed (%d)\n", ret);
    if (codec_ctx_) avcodec_flush_buffers(codec_ctx_);
    return ret;
}

int HardwareDecoder::seek_and_discard_to_target(int64_t target_pts_us) {
    if (!fmt_ctx_ || !codec_ctx_) return AVERROR(EINVAL);

    int64_t target_pts_tb = av_rescale_q(target_pts_us, AVRational{1, 1000000}, info_.time_base);
    int ret = avformat_seek_file(fmt_ctx_, info_.stream_index, 0, target_pts_tb, target_pts_tb, AVSEEK_FLAG_BACKWARD);
    if (ret < 0) ret = seek_to_start();
    if (codec_ctx_) avcodec_flush_buffers(codec_ctx_);

    bool eof = false;
    while (!eof) {
        AVFrame *frame = decode_next_frame(&eof);
        if (!frame) break;
        int64_t pts_us = (frame->pts != AV_NOPTS_VALUE)
                             ? av_rescale_q(frame->pts, info_.time_base, AVRational{1, 1000000})
                             : 0;
        if (pts_us >= target_pts_us) {
            av_frame_free(&frame);
            break;
        }
        av_frame_free(&frame);
    }
    return 0;
}

} // namespace daisy
