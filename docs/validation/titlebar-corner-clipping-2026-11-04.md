# Titlebar corner clipping — 2026-11-04

The native decoration previously filled a rectangular titlebar and removed its clip before painting the caption and buttons. Only the close-button hover material had a separate corner path. This allowed titlebar pixels to remain outside the compositor's rounded outline.

The complete titlebar now uses one rounded frame path intersected with its titlebar area and the caller's damage clip. Background, caption text, button backgrounds, and glyphs remain inside it. Button painting also applies the path independently, and close-hover clipping intersects it rather than replacing it. The curve uses a rounded rectangle instead of the former separate quadratic corner approximation.

Active/inactive radii follow the compositor's `Round-Corners` settings, including its defaults and bounds. Theme/config changes refresh the cached values. Maximized and tiled states use square geometry; KWin's [6.8 decoration implementation](https://github.com/KDE/kwin/blob/Plasma/6.8/src/decorations/decoratedwindow.cpp) exposes requested tile anchors through `adjacentScreenEdges()`. The compositor remains responsible for client-surface clipping and frame strokes.

Validation:

- Native Debug plugin build passed.
- Pixel clipping regression passed across 120 combinations: 100/125/150/200% scaling, narrow/normal widths, zero/ordinary/large radii, and outer/inset paths. Oversized caption text and danger-color fills do not escape the corners or titlebar area. Square corners stay opaque.
- Isolated KWin OpenGL checks passed at 100% and 125% scaling with the updated decoration and the existing frame effect. These cover server/client decorations, resize, maximize, fullscreen bypass, effect reconfiguration, reload, and teardown.
- Decoration package build/staged install checks passed. The existing script now also runs the pixel regression. Whitespace checks pass.

Reproduce:

```sh
bash scripts/test-kwin-window-decoration-install.sh
```

The pixel test renders the shared clipping path with QPainter; native KWin checks verify compositor lifecycle/state rather than screenshot pixel coverage. Physical GPU presentation was not certified, and the running user's decoration plugin was not replaced or reloaded. The fix requires deploying the rebuilt native decoration to affect existing desktop sessions.
