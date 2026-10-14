# Taskbar previews

Hovering a running app opens its current-workspace windows after 350 ms. Moving
between running apps switches previews immediately; a 250 ms grace period lets
the pointer travel into the preview. Clicking a card restores and activates its
window. The close button and middle click close that window. Wide groups scroll
horizontally with the wheel or a drag. Clicking a taskbar button, opening its
context menu or launcher, activating a window, or switching desktops dismisses
the preview.

The shell publishes immutable requests through `GetTaskbarPreview` on the
existing window bridge. A KWin declarative script polls every 150 ms and renders
live `WindowThumbnail` items, using the same rounded mask and window cropping
helpers as task overview. The popup does not take keyboard focus. It releases
thumbnail references after closing and dismisses if the shell stops replying.
Unavailable native previews fall back to the normal app tooltip.

Colors come from the shell's semantic theme brushes. Requests carry reduced
motion preferences and screen-relative taskbar bounds; KWin maps those bounds
to its logical output geometry and clamps the preview to the output. Native
Wayland panels publish surface-relative bounds plus their logical height:
Wayland cannot provide global client coordinates, so KWin resolves the bottom
panel's position against the output. The
installer uses content-specific QML paths to bypass stale component caches on
development reloads. `scripts/daisyos-native-development restart` installs the
package automatically.

Validation:

- `scripts/test-kwin-window-bridge-package.sh` checks package integration and
  matching overview rendering helpers.
- `QT_QPA_PLATFORM=offscreen /usr/lib/qt6/bin/qmltestrunner -input packaging/kwin/daisyos-taskbar-preview/tests`
  checks placement on small, negative-coordinate, top-panel, and crowded outputs.
- The headless `TaskbarPreviewTests` check delay, grouping, cancellation, tooltip
  suppression, and normal taskbar click behavior.
- A native KWin smoke fixture rendered a real VS Code thumbnail, captured at
  `artifacts/taskbar-preview/native-smoke.png`. This verifies native rendering;
  it does not establish physical pointer behavior across multiple monitors.
- A real uinput pointer test exercised the running shell's hover request and
  native preview: it opened above the icon, remained visible when entering the
  preview, and dismissed after leaving. The capture is
  `artifacts/taskbar-preview/native-hover-fixed.png`. Multiple physical monitors
  remain unverified.
