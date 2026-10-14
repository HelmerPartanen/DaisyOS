# Color and HDR

DaisyOS GUI colors use sRGB. Skia's OpenGL presentation targets, framebuffer
targets, and intermediate composition surfaces explicitly use sRGB so image
profiles are converted before presentation. Scaled image decoding and bitmap
resizing retain the source profile. This also covers wallpaper crop caches.
KWin maps the resulting SDR GUI into the display's HDR output; GUI surfaces
must not be labelled PQ just because the monitor has HDR enabled.

Native video wallpapers use a separate color-managed Wayland subsurface.
PQ/BT.2020 video uses a deep-color EGL buffer, with FP16 available when RGB10A2
is unavailable. The renderer negotiates the image description with KWin and
falls back to SDR conversion when HDR transport is unavailable.

Use `kscreen-doctor -o` to inspect each output. For faithful SDR colors, SDR gamut
wideness should be 0%. SDR brightness controls the GUI reference white in HDR,
not the video's peak luminance. Preserve user calibration and ICC profiles;
do not invent peak-brightness overrides when EDID omits them. Night Light
intentionally changes the display white point.

Verification:

- Run the Avalonia Skia `ImmutableBitmapTests` from `external/Avalonia` using
  its Microsoft Testing Platform runner. The color regression checks that
  linear gray is converted to sRGB through original, resized, and scaled
  decode paths.
- Run `python3 scripts/test-mpv-wallpaper-quality.py` for SDR video conversion.
- Run `src/native/daisy_mpv_wallpaper/build/daisy_mpv_surface_test` with a
  PQ/BT.2020 file and HDR argument `1` for live color negotiation, buffer depth,
  and presentation feedback.

These checks verify rendering and transport. Physical accuracy, display tone
mapping, and peak luminance require a calibrated display and measurements.
The GUI image path is SDR sRGB; native HDR video support does not imply HDR
still-image or gain-map decoding support.
