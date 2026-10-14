# Text rendering audit — 2026-10-08

The existing shared policy uses grayscale antialiasing, strong font hinting, aligned baselines and layout rounding. Skia maps these to antialiased glyph edging, full hinting and baseline snapping. This is appropriate for translucent shell surfaces without assuming an RGB subpixel order. Font sizes are integer logical sizes; TypographyPolicy measures line/control heights for the selected family and provides font fallback.

The native Wayland implementation prefers the compositor's fractional scale, updates render scaling when it changes, and uses viewporter destinations for logical surface size. It does not impose integer upscaling on a fractional-capable compositor. Repository scans found no text bitmap caching or explicit aliased/unhinted text overrides. Animated transforms can still change the appearance of text during motion; final clarity depends on display pixels and font design.

Found and fixed a coverage gap: the shared style now applies TextRenderingPolicy and layout rounding to native PopupRoot hosts as well as windows, user controls and tooltips. Native dropdown text therefore receives the same policy as the shell. The popup background animation exclusion remains unchanged.

The native popup regression now attaches its host to a styling parent, matching real popup creation. This uncovered an additional white-flash path in `PART_ContentPresenter`, which forwards the root background. Shared transitions are disabled on that native chrome presenter; the inner card and text controls retain their transitions.

Tests cover actual render scales 1, 1.25, 1.5, 1.75, 2 and 3, including changing scale on an existing window. They check framebuffer pixel dimensions, visible rasterized text, grayscale channels without color fringing, line fit, and configured options. Serif and monospace fonts are also exercised at fractional scales. Existing multilingual/larger-font Settings and confirmation layouts and native-popup/tooltip regressions are included.

The live display was read-only inspected: native 2560×1440 at scale 1. Its resolution and scale were not changed. Automated scale tests do not establish physical sharpness on every monitor or guarantee equal quality for every third-party font.

Reference: [Avalonia text options](https://docs.avaloniaui.net/docs/graphics-animation/text-options). Current repository source was used to verify the fork's exact option names and Skia implementation.

Twenty-six focused GUI tests passed, including existing theme synchronization. The larger-font Noto Sans sample was visually inspected with Finnish, Japanese, Arabic and Hindi text; no clipping was observed. Existing build/analyzer warnings outside this change remain.

Files, Notes and Settings native rebuilds completed with zero warnings/errors. The shell reloaded successfully (PID 39984), with KWin preserved (1042). Strict compositor preflight reported zero failures/warnings. Root and Avalonia whitespace checks passed. Existing system-app windows must be reopened to load the updated styles.
