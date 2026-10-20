# Native window menus

`daisyos-window-menus-v4` supplies the DaisyOS menu renderer for KWin-owned
`QMenu` widgets. This covers title-bar right-click, the window-menu button,
Alt+F3, Wayland `xdg_toplevel.show_window_menu`, XWayland window-menu requests,
and their nested menus. It does not restyle menus inside third-party processes.

KWin retains the original action objects, window targets, enabled and checked
states, workspace/output submenus, pointer/keyboard handling and dismissal.
The effect never intercepts input, infers an action target from focus, or inhibits
direct scanout. It changes only native menu rendering. No KDE menu widget
painting is used for the frame, menu rows, state checkmarks or submenu arrows.

`WindowDecorationThemeService` exports a `[Menus]` section from the current
DaisyOS semantic resources to `daisyos-window-decorationrc`. Native menus follow
theme, system font and text-size changes, including already constructed menus.
The native material preserves the context-menu brush opacity and publishes a
rounded `kwin_blur` RegionF on each internal QWindow, including submenus. Frame
rings are painted separately so they do not make the material opaque.
The shared HugeIcons assets are tinted from their alpha and cached at the
current display scale. KWin operation IDs provide theme-independent icon lookup.

Development installation: `scripts/install-window-menus.sh`. This atomically
installs the native library and shared icons, enables the effect, and loads it
when KWin can discover it. KWin can retain already loaded plugin code; binary
updates to an existing effect ID may require a new login. Theme changes do not.
The development-session launcher, publish pipeline, ISO staging and Arch package
include the renderer and icons. The installer disables the earlier v1 renderer.

Diagnostics:

```sh
qdbus6 org.kde.KWin /Effects org.kde.kwin.Effects.isEffectLoaded daisyos-window-menus-v4
qdbus6 org.kde.KWin /org/daisyos/WindowMenus org.daisyos.WindowMenus1.State
```

Validation uses `WindowMenuThemeTests` for light/dark semantic exports at 100%
and 150% text sizes. `daisy-menu-preview CONFIG OUTPUT.png` renders real Qt
menus and verifies action identity, shortcut, checked and disabled states;
`DAISY_MENU_PREVIEW_SCALE` chooses the rendering scale. The interactive preview
opens a temporary native window for live compositor checks and closes after
three minutes. Close that window after acceptance.
