#ifndef DAISY_DMABUF_EXPORTER_H
#define DAISY_DMABUF_EXPORTER_H

#include "../include/daisy/live_wallpaper.h"
#include "hw_decoder.h"

extern "C" {
#include <libavutil/frame.h>
#include <libavutil/hwcontext_drm.h>
#include <libswscale/swscale.h>
}

namespace daisy {

enum class TopologyMode {
    SameDeviceZeroCopy,
    CrossDeviceDmabuf,
    CrossDeviceGpuCopy
};

enum class SyncMode {
    Explicit,
    ImplicitPolled
};

class DmabufExporter {
public:
    DmabufExporter() = default;
    ~DmabufExporter();

    void init(AVHWDeviceType hw_type, StatsCollector &stats);
    void set_output_size(int width, int height);

    bool export_frame(AVFrame *src_frame, DaisyVideoFrame *out_frame, StatsCollector &stats);

    TopologyMode topology_mode() const { return topology_mode_; }
    SyncMode sync_mode() const { return sync_mode_; }
    const char *topology_string() const;
    const char *sync_mode_string() const;

private:
    void reset_sws_context();

    TopologyMode topology_mode_ = TopologyMode::SameDeviceZeroCopy;
    SyncMode     sync_mode_     = SyncMode::Explicit;
    uint64_t     negotiated_modifier_ = 0; // DRM_FORMAT_MOD_INVALID

    struct ::SwsContext *sws_ctx_ = nullptr;
    int sws_src_w_ = 0;
    int sws_src_h_ = 0;
    int sws_src_fmt_ = -1;
    int sws_dst_w_ = 0;
    int sws_dst_h_ = 0;
    int output_width_ = 0;
    int output_height_ = 0;
};

} // namespace daisy

#endif // DAISY_DMABUF_EXPORTER_H
