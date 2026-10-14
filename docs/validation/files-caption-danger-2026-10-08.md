# Files captions and danger colors — 2026-10-08

Files caption buttons previously had the standard 32px control height within a taller tab/title row. Their explicit height is removed and vertical alignment stretches to the row, eliminating the top gap while preserving centered glyphs.

Filled danger actions now have separate semantic red roles: rest `#C92A35`, hover `#B8212C`, pressed `#A51925`, with white content. White-content contrast is respectively 5.43:1, 6.38:1 and 7.59:1. Error text retains its own theme-aware role; dark error text uses a more saturated red, and destructive menu labels/icons consume that role instead of the accent swatch. Close-button hover/pressed states use filled roles in Files, shared app chrome, Calendar, taskbar previews and exported native decorations. Files close glyphs now turn white on hover/press; Calendar's hover icon selector is corrected.

Fourteen focused GUI checks passed, covering rendered Files caption bounds, light/dark sidebars, native caption state, larger-font multilingual layouts and theme synchronization. Caption assertions compare all three buttons with their actual title-row container and require no vertical offset.

Core tests passed 384 with one opt-in live screenshot skip. Files, Notes and Settings native rebuilds completed with zero warnings/errors. Shell reload preserved KWin and strict compositor preflight returned zero failures/warnings. Whitespace checks passed. Existing Files processes must be reopened to load their new caption geometry and colors; physical appearance remains a user acceptance check.
