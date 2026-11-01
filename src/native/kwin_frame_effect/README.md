# Daisy window frame

This is the in-repository replacement for the downloaded Rounded Corners
plugin. It uses the unique `daisyframe` plugin ID and retains the `Round-Corners`
configuration group for shell theme exports. The installer disables and unloads
the legacy plugin to avoid duplicate borders and cached legacy libraries.
The shader is embedded in the plugin; loose `shapecorners*.frag` files from
older installs are unused.

The effect targets the installed KWin 6.8 Wayland private API. Rebuild after
KWin upgrades. The installer fingerprints all sources and the KWin version,
checks installed plugin integrity, and publishes the shared object atomically.
It still bootstraps pinned ECM and Vulkan build headers when needed; effect
source is never downloaded or patched.

Normal windows and dialogs receive two inset bands (one logical pixel each by
default). Native shadows are retained outside the window rectangle. Tiled and
maximized frames are square; fullscreen windows bypass the effect. Layer-shell
surfaces, internal windows and lock screens are excluded by native type rather
than by process name or title heuristics. No decoration settings are rewritten
by the effect on load/unload. Malformed colors and non-finite dimensions fall
back to defaults; widths and radius are bounded. The supported configuration
is Size/InactiveCornerRadius, active/inactive OutlineThickness,
SecondOutlineThickness, OutlineColor, SecondOutlineColor, their separate alpha
values, IncludeNormalWindows, IncludeDialogs and Exclusions. Legacy animation,
gradient, outer-outline and per-state disable options are not implemented.

Install/update: `scripts/install-kwin-frame-effect.sh`.

Validation:

- `scripts/test-kwin-frame-shader.py`: actual surfaceless EGL/OpenGL pixel tests;
  requires Mesa EGL and desktop OpenGL. KWin color helpers are identity stubs.
- `scripts/test-kwin-frame-effect-integrity.py`: isolated install metadata tests.
- `scripts/test-kwin-frame-effect-runtime.sh /path/to/daisyframe.so`:
  isolated KWin, private D-Bus/config/plugin path, real KWin shader helpers,
  SSD and frameless clients, resize/maximize/fullscreen, reload and destruction.
  Requires KWin, qdbus6 and PySide6; logs go to artifacts/compositor/frame-effect.

These tests do not establish compatibility with every GPU, third-party CSD
layout or other compositor effect. In particular, apps that draw invisible
padding inside their frame geometry may need application-specific work.
