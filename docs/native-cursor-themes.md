# DaisyOS native cursor themes

The supplied `Assets/Cursor/Dark` and `Light` Windows CUR/ANI files are converted
to standard Xcursor files using Qt's ICO decoder and libXcursor's native writer.
KWin owns cursor rendering and animation. There is no pointer overlay, polling
of pointer positions, or application-specific cursor replacement.

Four themes are installed: **Dark (Default)**, **Light (Default)**, and the supplied
**Small** artwork variant of each. The Small artwork is a separate theme; cursor
pixel size remains independently adjustable. Select a theme and size in Settings
> Input. Dark is the new-install default. Existing explicitly selected themes
remain saved. This implementation does not change cursor theme automatically
when changing the GUI theme.

Each shape includes 24, 32, 48, 64, 96 and 128 pixel images. The nearest available
source at or above the requested size is preferred. Only missing resolutions
are smoothly resampled; the source hotspot is scaled with the image. Qt decodes
CUR transparency and monochrome AND masks. Pixels are saved as premultiplied
ARGB, avoiding dark/opaque transparency artifacts.

ANI frames retain their original sequence, per-frame rates, and hotspots.
Cumulative rounding converts Windows 1/60-second jiffies to Xcursor milliseconds
without drifting the full animation loop. The supplied busy and working
animations contain 40 frames over 2 seconds. `wait`/`watch` and
`progress`/`left_ptr_watch` are native animated cursors used by KWin and the
DaisyOS task feedback effect.

The supplied assets cover the arrow, text, hand, help, crosshair, unavailable,
resize, move, up arrow, pen, person and pin shapes. Standard Qt/GTK/X11 aliases
are included. Shapes absent from the supplied pack, such as grab, drag-copy and
zoom, inherit Breeze to retain the correct interaction meaning. This requires
the existing `breeze_cursors` theme; missing artwork is not replaced by an
incorrectly labeled supplied cursor.

```bash
scripts/install-cursor-themes.sh
python3 scripts/test-cursor-themes.py
python3 scripts/test-busy-cursor-runtime.py --expiry
```

Build dependencies are Python 3, PySide6 (Qt image decoding) and libXcursor.
The Windows source assets are excluded from the Avalonia resource bundle;
only the converted native themes are deployed. Native Development installs the
themes before starting KWin. Publishing includes
them; ISO staging installs them in the system icon directory with cursor defaults.
The converter uses the [native Xcursor image/hotspot/delay model](https://xorg.freedesktop.org/archive/X11R7.5/doc/man/man1/xcursorgen.1.html).

Verified on 2026-10-13: every supplied asset converts, source hotspots and frame
rates match the generated files, alpha is premultiplied, aliases resolve, native
loading animation advances, task overlap/cancellation/disconnect/watchdog cleanup
works, and light/dark/Small theme and 24/32/48 pixel size changes work while the
spinner is active. A compositor screenshot shows the supplied native artwork.
Six focused Settings tests and the native launcher integration suite passed.
A live fixture confirmed that an already-running launch service passes a newly
selected theme and size to a child process, and systemd activation values were
verified independently.

The KDE cursor config, GTK preferences, D-Bus/systemd activation environment,
and persistent KIO launcher environment are synchronized when the cursor changes.
The source Windows files remain unchanged.

Physical mixed-DPI multi-monitor movement and legacy app compatibility require
separate checks; these tests do not establish that every application obeys theme
changes without being reopened.
