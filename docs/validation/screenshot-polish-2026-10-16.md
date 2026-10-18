# Screenshot tool validation, 2026-10-16

The toolbar uses the taskbar material and frame brushes. The frozen screenshot
occupies a separate passive surface so KWin can blur it behind the toolbar.
The mode container has a 14px radius around 12px buttons. Small viewports and
large text use compact mode buttons and wrapping instructions.

Startup transfers an uncompressed BMP from the native helper and prepares the
snapshot on a worker. Saved screenshots remain PNG. Cursor-enabled captures
use a fresh capture. Cancellation owns worker pixels until encoding finishes;
failed captures preserve the selection for retry. Toolbar avoidance uses a
spring transition, while direct dragging follows the pointer.

Validation:

- 10 screenshot UI tests passed, covering themes, compact layouts, 200% text,
  selection, keyboard adjustment, modes, dragging, countdown, cancellation,
  save failure, and retry.
- 18 screenshot service tests passed with the newly installed native helper,
  including real KWin screen, window, and area captures without fallback.
- Native helper staging and installer checks passed.
- Three standalone BMP transfer probes took 1525, 1479, and 1475ms. These
  include standalone process/session overhead and are not hotkey latency.
- Shell startup logging measures capture, decode, and render readiness;
  render readiness does not establish physical presentation latency.
- Final shell build passed with zero warnings/errors. A concurrent clock test
  initially locked the Avalonia PDB; the serialized retry passed.
- Live region toolbar inspection passed after moving the passive backdrop to
  the compositor top layer beneath the overlay toolbar. The captured toolbar
  is saved in `artifacts/screenshot-polish-2026-10-16/toolbar-live.png`.
- Recent startup samples ranged from 409 to 1865ms to render readiness; the
  final sample was 800ms (649ms capture, 48ms preparation). Startup remains
  dependent on compositor capture latency and session load.

Physical hotkey presses, cross-application clipboard paste, and exhaustive
multi-monitor configurations have not been certified by these checks.
