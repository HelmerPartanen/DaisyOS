# Daisy window frame

The `daisyframe` plugin uses KWin 6.8's native scene graph: `Window::borderRadius`
clips client content, and two `OutlinedBorderItem` nodes draw inset bands inside
the frame. KWin handles pixel snapping, color management, opacity, damage and
window transformations. There is no offscreen texture, custom fragment shader,
or second corner mask. Both SSD and CSD windows use this path. The decoration
provides title bars, resize semantics and shadows without duplicating outlines.

The shell configures a single one-pixel border using `StrokeDefaultColor`: dark
in the light theme, light in the dark theme, with the same opacity as dividers.
The optional second band is disabled. The border overlays the inside edge of the frame without changing window
geometry or input coordinates.
Tiled and maximized windows are square and omit both bands to avoid
seams over neighbouring windows and panels. Fullscreen windows bypass styling.
Layer-shell, internal and lock-screen surfaces are excluded by native type.

Window deletion, fullscreen transitions, exclusions and plugin unload remove
scene nodes and restore the previous corner radius, unless another owner has
since changed it. Native shadows are preserved. Malformed colors and non-finite
numbers fall back to defaults; widths and radii are bounded.

The `Round-Corners` configuration group remains compatible with shell theme
exports: Size/InactiveCornerRadius, active/inactive OutlineThickness,
SecondOutlineThickness, OutlineColor, SecondOutlineColor, separate alpha values,
IncludeNormalWindows, IncludeDialogs and Exclusions. Legacy gradient, animation,
outer-outline and per-state disable options are not implemented.

Install/update with `scripts/install-kwin-frame-effect.sh`. The installer disables
the old Rounded Corners plugin, fingerprints the sources and installed KWin
version, verifies plugin integrity, and publishes the library atomically.
Rebuild after KWin upgrades; the effect targets KWin 6.8 private APIs.

Validation:

- `python3 scripts/test-kwin-frame-effect-integrity.py`: isolated install metadata.
- `scripts/test-kwin-frame-effect-runtime.sh /path/to/daisyframe.so [decoration.so]`:
  isolated KWin with private D-Bus/config/plugin paths; SSD and frameless clients,
  resize, maximize, fullscreen, reload and destruction. Requires KWin, qdbus6 and
  PySide6. Set `DAISY_TEST_SCALE=1.25` (or 1.5/2) to exercise scaled outputs.
  Checks inspect native band count, radii, dimensions and restoration across
  reloads. Logs go to artifacts/compositor/frame-effect.

Applications with invisible padding inside their frame geometry may still need
application-specific integration. Runtime tests do not establish compatibility
with every GPU or third-party effect.
