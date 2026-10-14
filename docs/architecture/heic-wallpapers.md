# HEIC wallpapers

The wallpaper picker accepts `.heic` and `.heif` images. Desktop and lock-screen
wallpaper layers and the Settings preview use the same selected image.

The original file is retained. libheif decodes only the selected top-level image
to an 8-bit RGBA PNG in `$XDG_CACHE_HOME/DaisyOS/heic-wallpapers` (or
`~/.cache/DaisyOS/heic-wallpapers`). Source path, size, modification time, and image
ID identify cached frames. Conversions happen off the UI thread; old frames stay
visible while a replacement loads. The disk cache has a 512 MiB budget.

Apple dynamic desktop XMP is supported in element and attribute forms:

- `apple_desktop:apr`: select the light/dark image using the active appearance.
- `apple_desktop:h24`: select the frame for the local time, wrapping at midnight.
- `apple_desktop:solar`: select the nearest sun-position frame using the same
  timezone-derived location as automatic appearance; this is an approximation
  rather than GPS geolocation. Appearance fallbacks are honored when available.

Appearance changes trigger a refresh immediately, and a one-minute timer checks
timed images while the layer is loaded. An unchanged selected frame reuses the
existing rendered bitmap. Static HEIC files use their primary image. Missing or
unrecognized dynamic metadata falls back to the primary image.

The ISO includes `libheif` and `libde265`. HEIC wallpaper support requires a working
HEVC decoder in libheif. This path is for SDR still images; it does not establish
HDR or wide-gamut display correctness.

Format references: [dynamic desktop metadata](https://gist.github.com/leptos-null/c88e8c3c0f9f6860c7500826031c8551)
and [h24 producer](https://github.com/GeneralD/lyra-dynamic-wallpaper).
