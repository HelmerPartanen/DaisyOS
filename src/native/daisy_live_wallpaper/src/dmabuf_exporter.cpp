#include "dmabuf_exporter.h"
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <unistd.h>
#include <fcntl.h>
#include <sys/stat.h>
#include <limits>
#include <cmath>
#include <algorithm>

extern "C" {
#include <libavutil/hwcontext.h>
#include <libavutil/hwcontext_drm.h>
#include <libavutil/imgutils.h>
#include <libswscale/swscale.h>
#include <drm_fourcc.h>
}

namespace daisy {

DmabufExporter::~DmabufExporter() {
    reset_sws_context();
}

void DmabufExporter::reset_sws_context() {
    if (sws_ctx_) {
        sws_freeContext(sws_ctx_);
        sws_ctx_ = nullptr;
    }
    sws_src_w_ = 0;
    sws_src_h_ = 0;
    sws_src_fmt_ = -1;
    sws_dst_w_ = 0;
    sws_dst_h_ = 0;
}

void DmabufExporter::init(AVHWDeviceType hw_type, StatsCollector &stats) {
    reset_sws_context();
    // Detect multi-GPU topology once per session (§3)
    struct stat render_st{}, card_st{};
    bool has_render = (stat("/dev/dri/renderD128", &render_st) == 0);
    bool has_card   = (stat("/dev/dri/card0", &card_st) == 0);

    if (has_render && has_card && (render_st.st_rdev != card_st.st_rdev)) {
        topology_mode_ = TopologyMode::CrossDeviceDmabuf;
    } else {
        topology_mode_ = TopologyMode::SameDeviceZeroCopy;
    }

    sync_mode_ = SyncMode::Explicit;

    std::fprintf(stderr, "[daisy-video] Hardware decode is available; using bounded CPU presentation frames.\n");
}

void DmabufExporter::set_output_size(int width, int height) {
    output_width_ = width;
    output_height_ = height;
}

const char *DmabufExporter::topology_string() const {
    switch (topology_mode_) {
        case TopologyMode::SameDeviceZeroCopy: return "same-device zero-copy";
        case TopologyMode::CrossDeviceDmabuf:   return "cross-device dmabuf";
        case TopologyMode::CrossDeviceGpuCopy:  return "cross-device gpu-copy";
    }
    return "same-device zero-copy";
}

const char *DmabufExporter::sync_mode_string() const {
    switch (sync_mode_) {
        case SyncMode::Explicit:       return "explicit";
        case SyncMode::ImplicitPolled: return "implicit-polled";
    }
    return "explicit";
}

bool DmabufExporter::export_frame(AVFrame *src_frame, DaisyVideoFrame *out, StatsCollector &stats) {
    if (!src_frame || !out) return false;

    std::memset(out, 0, sizeof(*out));
    for (int i = 0; i < DAISY_MAX_PLANES; ++i) out->fd[i] = -1;
    out->acquire_fence = -1;
    const int source_width = src_frame->width;
    const int source_height = src_frame->height;
    if (source_width <= 0 || source_height <= 0) return false;

    // Decode at source quality, but only materialise as many BGRA pixels as the
    // display can show. Avalonia currently consumes CPU-backed frames here; the
    // old DMA-BUF export was never imported by it and added driver pressure,
    // duplicated file descriptors, and a second buffer allocation per frame.
    const int requested_width = output_width_ > 0 ? output_width_ : source_width;
    const int requested_height = output_height_ > 0 ? output_height_ : source_height;
    // Match Avalonia's UniformToFill contract: scale to cover the output while
    // retaining the encoded aspect ratio. The Image control then performs the
    // small centered crop, exactly as it does for still wallpapers.
    // Downscale only: enlarging a low-resolution source here adds transfer
    // and allocation cost without recovering detail. Skia handles enlargement.
    const double scale = std::min(1.0, std::max(
        static_cast<double>(requested_width) / source_width,
        static_cast<double>(requested_height) / source_height));
    const int target_width = static_cast<int>(std::ceil(source_width * scale));
    const int target_height = static_cast<int>(std::ceil(source_height * scale));
    constexpr int kMaxPresentationDimension = 7680;
    constexpr int64_t kMaxPresentationBytes = 256LL * 1024 * 1024;
    if (target_width <= 0 || target_height <= 0 ||
        target_width > kMaxPresentationDimension || target_height > kMaxPresentationDimension ||
        static_cast<int64_t>(target_width) * target_height * 4 > kMaxPresentationBytes) {
        return false;
    }

    out->width  = target_width;
    out->height = target_height;

    if (src_frame->pts != AV_NOPTS_VALUE) {
        out->pts_us = src_frame->pts;
    } else {
        out->pts_us = 0;
    }

    AVFrame *sw_frame = src_frame;
    AVFrame *mapped_sw_frame = nullptr;

    // Hardware frame path (VA-API, CUDA/NVDEC, DRM PRIME). Transfer once into
    // a software frame for Avalonia's current CPU presentation path.
    if (src_frame->format == AV_PIX_FMT_DRM_PRIME || src_frame->format == AV_PIX_FMT_VAAPI || src_frame->format == AV_PIX_FMT_CUDA) {
        mapped_sw_frame = av_frame_alloc();
        if (mapped_sw_frame) {
            if (av_hwframe_transfer_data(mapped_sw_frame, src_frame, 0) == 0) {
                sw_frame = mapped_sw_frame;
            } else {
                av_frame_free(&mapped_sw_frame);
                mapped_sw_frame = nullptr;
                return false;
            }
        } else {
            return false;
        }
    }

    // Convert sw_frame to BGRA32 buffer for UI presentation
    if (sw_frame && sw_frame->data[0]) {
        const int buf_size = av_image_get_buffer_size(AV_PIX_FMT_BGRA, target_width, target_height, 1);
        if (buf_size <= 0 || buf_size > kMaxPresentationBytes) {
            if (mapped_sw_frame) av_frame_free(&mapped_sw_frame);
            return false;
        }
        uint8_t *bgra_buffer = static_cast<uint8_t *>(std::malloc(buf_size));
        if (bgra_buffer) {
            int src_fmt = static_cast<int>(sw_frame->format);
            if (!sws_ctx_ || sws_src_w_ != source_width || sws_src_h_ != source_height ||
                sws_src_fmt_ != src_fmt || sws_dst_w_ != target_width || sws_dst_h_ != target_height) {
                if (sws_ctx_) sws_freeContext(sws_ctx_);
                sws_ctx_ = sws_getContext(
                    source_width, source_height, static_cast<AVPixelFormat>(src_fmt),
                    target_width, target_height, AV_PIX_FMT_BGRA,
                    SWS_BICUBIC | SWS_ACCURATE_RND, nullptr, nullptr, nullptr);
                sws_src_w_   = source_width;
                sws_src_h_   = source_height;
                sws_src_fmt_ = src_fmt;
                sws_dst_w_   = target_width;
                sws_dst_h_   = target_height;
            }

            if (sws_ctx_) {
                // Hardware transfer does not carry frame properties; use the
                // original decoded frame for color metadata on every frame.
                int colorspace = SWS_CS_ITU601;
                switch (src_frame->colorspace) {
                    case AVCOL_SPC_BT709: colorspace = SWS_CS_ITU709; break;
                    case AVCOL_SPC_BT2020_NCL:
                    case AVCOL_SPC_BT2020_CL: colorspace = SWS_CS_BT2020; break;
                    case AVCOL_SPC_UNSPECIFIED:
                        colorspace = source_height >= 720 ? SWS_CS_ITU709 : SWS_CS_ITU601;
                        break;
                    default: break;
                }
                const auto format = static_cast<AVPixelFormat>(src_fmt);
                const bool full_range = src_frame->color_range == AVCOL_RANGE_JPEG ||
                    format == AV_PIX_FMT_YUVJ420P || format == AV_PIX_FMT_YUVJ422P ||
                    format == AV_PIX_FMT_YUVJ444P;
                const int *coefficients = sws_getCoefficients(colorspace);
                sws_setColorspaceDetails(sws_ctx_, coefficients, full_range ? 1 : 0,
                    coefficients, 1, 0, 1 << 16, 1 << 16);

                uint8_t *dst[4] = { bgra_buffer, nullptr, nullptr, nullptr };
                int dst_stride[4] = { target_width * 4, 0, 0, 0 };
                if (sws_scale(sws_ctx_, sw_frame->data, sw_frame->linesize, 0, source_height, dst, dst_stride) <= 0) {
                    std::free(bgra_buffer);
                    if (mapped_sw_frame) av_frame_free(&mapped_sw_frame);
                    return false;
                }

                // Store the CPU BGRA pointer in stride[0]/stride[1]. This is
                // the only ownership contract exposed to Avalonia today.
                uintptr_t ptr = reinterpret_cast<uintptr_t>(bgra_buffer);
                out->fd[0] = -2;
                out->pixel_format = DRM_FORMAT_ARGB8888;
                out->n_planes = 1;
                out->modifier = DRM_FORMAT_MOD_LINEAR;
                out->is_hardware = 0;
                stats.zero_copy = 0;
                stats.dmabuf_active = 0;
                out->stride[0] = static_cast<uint32_t>(ptr & 0xFFFFFFFF);
                out->stride[1] = static_cast<uint32_t>((ptr >> 32) & 0xFFFFFFFF);
                out->stride[2] = static_cast<uint32_t>(target_width * 4);
                out->stride[3] = static_cast<uint32_t>(target_height);
            } else {
                std::free(bgra_buffer);
            }
        }
    }

    if (mapped_sw_frame) av_frame_free(&mapped_sw_frame);
    return out->fd[0] == -2;
}

} // namespace daisy
