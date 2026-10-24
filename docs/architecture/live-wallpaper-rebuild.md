# Color-managed GPU live wallpapers

The development default is now libmpv, rendered directly into a renderer-owned Wayland subsurface behind each existing wallpaper host. Desktop controls and lockscreen foreground remain in Avalonia. `DAISYOS_VIDEO_BACKEND=legacy` is the development rollback switch; the old exporter remains solely for that switch until the release acceptance matrix is complete.

## Rendering and ownership

The pinned Avalonia fork exposes `LayerShellWindow.AttachNativeChildAsync`. Its factory runs on the Wayland worker and borrows the live display and parent surface. An idempotent host lease destroys the native player before the parent disconnects. The player owns a private Wayland event queue, EGL context, control thread and render thread. Render callbacks only signal pending work; renderer operations never wait on application control calls.

libmpv handles demuxing, dependency decoding, hardware selection and software fallback. Playback never streams CPU bitmaps or performs GPU readback. One-shot previews use the same GPU color renderer with an SDR sRGB target and one readback. Audio, subtitles, user configuration, scripts and external playlist references are disabled.

Presentation is capped at 30 fps on mains and 24 fps on battery when the user has explicitly allowed battery playback. It is paced by libmpv deadlines and compositor frame callbacks. Intentional source-frame skips complete playback slots without rendering. `wp_presentation` counters report actual presentation/discard events separately from submission. The implementation normalizes the 0.41 renderer's nanosecond frame timestamps against its clock despite the public header's microsecond description.

Fully hidden hosts pause the decoder. A withheld compositor frame callback also pauses decoding after 250 ms, covering delayed shell occlusion tracking. Output geometry updates use a viewport and physical EGL size. Host failure retains a color-correct poster and attempts one session recreation. Source replacement retains the old wallpaper until the first valid GPU buffer is submitted. The parent becomes transparent at that handoff; lockscreen scrim and foreground blur remain separate.

## Color policy

Explicit stream/container color descriptions take precedence over conflicting decoded-frame tags. Missing or unsupported fields are left to libmpv's documented inference. The selected Sequoia MOV exposes the relevant conflict: its container reports Display P3/gamma 2.2, but decoded frames report BT.709. The bridge supplies a metadata-only `format` filter; GPU rendering performs the actual transformation.

Wide-gamut SDR remains SDR. When the compositor supports the source's P3 or BT.2020 primaries, the subsurface retains them and KWin performs output mapping. SDR previews are converted to sRGB. Native surfaces use 10-bit or FP16 buffers for high-precision input when supported; NVIDIA on this machine supplies FP16 windows but no RGB10A2 windows. The shell's UI surfaces keep their existing format.

HDR10 and HLG are rendered as BT.2020/PQ and attached with a matching `wp_color_manager_v1` image description. Static mastering/content-light metadata is carried when present; an absent mastering peak defaults to 1000 nits. KWin maps that content to the configured output, including SDR output. If protocol or pixel-format negotiation fails, the renderer produces SDR with fixed metadata-based tone mapping. `hdr_active` describes HDR-encoded surface content, not whether the physical monitor is operating in HDR mode. Native HDR output does not enable HDR in the user's display settings.

Intermediate rendering uses FP16. Sharpening, debanding, interpolation, dynamic peak detection and temporal dithering are disabled. Final quantization uses the actual surface precision. Full Dolby Vision enhancement-layer processing is outside this replacement.

## Optimized copies and Settings

Sources above 30 fps or above the 1920×1080 playback pixel budget are automatically optimized before continuous playback begins. DaisyOS presents a color-correct still frame immediately while the low-priority copy is prepared, so pathological sources never become the steady-state decoder. Preparation publishes cross-process progress for diagnostics and future UI surfaces. The saved wallpaper URI always names the original.

Copies retain duration, encoded orientation, color tags and static HDR metadata. Their dimensions retain aspect ratio, never upscale and are bounded to a 1080p pixel budget. Encoding uses a 30 fps libx265 Main 10 stream at CRF 18 with two CPU workers. Input is rate-limited to one quarter of real time, and the decoder, filters and encoder share a two-CPU affinity mask under the lowest CPU and I/O priorities. Frame metadata is explicitly applied before/after scaling so FFmpeg does not overwrite container color tags with conflicting decoded-frame tags.

When a host moves to a larger physical target, preparation returns to the original source rather than enlarging its existing derivative. A safe source can continue playing during a resize conversion; an over-budget source remains on its still preview. Recovery re-probes original-source metadata. Copy handoff preserves playback position; subsequent loops start at the beginning.

Only one conversion runs at a time. Identical source/geometry requests share a job; cancelling the last consumer kills obsolete work. Visibility demands are counted across hosts, and fully hidden sources suspend preparation. A parent-death signal terminates even a suspended conversion after shell failure. Completed files are verified and atomically published. Cache keys include streamed SHA-256, geometry and conversion version. Cache storage is limited to 5 GiB with 2 GiB free-space reserve, and active files are protected from eviction. Failure keeps the original playable.

## Build and validation

- `scripts/build-mpv-wallpaper.sh` builds the bridge against libmpv API >=2.4.
- `scripts/build-local-mpv.sh` optionally builds pinned mpv 0.41.0 and NVIDIA codec headers under the user's data directory, with archive checksums and retained source notices. It changes no system packages.
- Native development restart and publishing build the bridge. Runtime mpv/FFmpeg are already declared in the desktop/ISO package lists. Publishing writes the resolved dependency versions into `wallpaper-dependencies.txt`.
- `scripts/test-mpv-wallpaper-quality.py` compares GPU previews with an independent linear-light zscale reference for BT.601/709/P3, full/limited range and 8/10-bit input, and checks repeat-frame stability.
- `daisy_mpv_surface_test /absolute/video 30 [hdr-kind]` checks live Wayland presentation, pause/resume, resize, bounded cadence and cleanup. Fixtures must carry color properties on frames as well as codec options; FFmpeg 9 can otherwise omit those tags from generated samples.
- Focused managed tests cover source metadata, HDR classification, cover sizing, conversion fidelity, original preservation, cache reuse and playable-video validation.

Live evidence is stored under `artifacts/live-wallpaper-2026-10-12/`. Physical HDR display acceptance, mixed HDR/SDR multi-output acceptance and the complete failure/interaction matrix remain release gates. The current monitor/session is SDR; protocol acceptance of an HDR surface does not establish physical HDR correctness.

### Resumed validation, 12 October 2026

- All ten focused video metadata, optimization and playback service tests passed after the changes.
- Five lockscreen layout/clock-blur tests passed after updating two stale picker test references to the current API. These are headless layout checks; native lock/unlock remains unverified.
- Twelve SDR GPU preview cases passed against zscale, with maximum channel error 1/255 and byte-identical repeat renders (`resumed-color-checks.log`).
- The actual desktop shell's open file handle confirms adoption of the verified 2560×1440, 60 fps Sequoia derivative; its persisted source remains the original MOV. Desktop captures show video behind shell controls and advancing between captures (`resumed-desktop.png`, `resumed-visible-desktop.png`). The editor still covers part of the desktop; these captures do not validate fullscreen occlusion or lock/unlock.
- HDR10 and HLG one-second fixtures each completed a thirty-second run with over twenty loops, native HDR surface encoding, pause/resume, resize, and zero compositor discard reports (`resumed-hdr-loops.log`, `resumed-hlg-loops.log`). This tests surface negotiation on the SDR monitor.
- A ten-second shell sample used 20.2% of one CPU core, with RSS increasing by 2.9 MB (`resumed-resource-sample.json`). A concurrent native test was running. This is a diagnostic sample, not an isolated performance comparison or a memory-growth acceptance result.
- The earlier five-minute optimized surface run reported 17,673 presented buffers and zero discard reports. Its maximum interval reached 428 ms, so zero discards alone does not establish the under-1% presentation-miss gate. Static/legacy/standalone comparisons and long-run full-output pacing still require acceptance measurements.

Publishing now records named dependency versions for successful publishes, including when the bridge uses the optional local libmpv prefix. The pinned Avalonia native-child changes remain inside the modified submodule and must be included in the release revision.

The optional mpv build disables GPL code in mpv itself, but linked FFmpeg/package components have their own license requirements. This checkout has no root distribution license. Distribution license compatibility and complete third-party notices must be resolved before a release; a local development build is not a legal compatibility certification.

Primary references: [mpv render API](https://raw.githubusercontent.com/mpv-player/mpv/v0.41.0/include/mpv/render.h), [mpv format filter](https://github.com/mpv-player/mpv/blob/master/DOCS/man/vf.rst), [Wayland color management](https://raw.githubusercontent.com/wayland-mirror/wayland-protocols/main/staging/color-management/color-management-v1.xml).
