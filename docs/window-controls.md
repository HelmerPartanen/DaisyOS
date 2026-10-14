# Window Controls

Window controls provide a compact interface for managing running application windows from within the DaisyOS shell. The current Alt+Tab overlay and Dock track applications launched by DaisyOS; compositor-authoritative external window control remains planned.

## Planned Features

### Dock Window Previews

Hovering over a running app in the dock will show a thumbnail preview of all open windows for that application. Clicking a preview brings that window to focus.

### Window Switcher (Alt+Tab) — Prototype available

The shell now shows DaisyOS-tracked running applications in a compact Alt+Tab popup. Repeated Tab or Shift+Tab changes selection and releasing Alt activates it. Window titles, thumbnails, and applications launched outside DaisyOS require the future KWin bridge.

### Snap / Tile Assistant

When dragging a window near a screen edge, a snap/tile overlay appears, letting you snap the window to half, quarter, or full screen. This integrates with KWin's tiling system.

### Minimize, Maximize, Close

The dock's running app context menu (right-click) provides:

- **Minimize** — hide the window
- **Maximize** — toggle fullscreen within the screen
- **Close** — quit the application

### Pipette Mode

A "pipette" tool accessible from the system bar that lets you click any window to:

- View its process name and PID
- Force-close it
- Move it to a different virtual desktop

## Service Interface

```csharp
public interface IWindowControlService
{
    IReadOnlyList<WindowHandle> GetOpenWindows();
    Task FocusWindowAsync(WindowHandle handle);
    Task CloseWindowAsync(WindowHandle handle);
    Task MinimizeWindowAsync(WindowHandle handle);
    Task MaximizeWindowAsync(WindowHandle handle);
}
```

## Dependencies

- **X11**: `_NET_CLIENT_LIST`, `_NET_ACTIVE_WINDOW`, `_NET_CLOSE_WINDOW`
- **Wayland / KWin**: KWin scripting or D-Bus interface for window operations

## Future Work

- Virtual desktop integration (move windows between desktops).
- Window tabbing / grouping.
- Tiling layout presets.
