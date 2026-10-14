# Tooltip corners — 2026-10-08

The Wayland backend was applying the rounded menu blur mask to every xdg popup. That mask assumes a 16px menu gutter and 14px menu radius; small tooltips can fall back to a full rectangular region, including their transparent corners. Tooltips already render an opaque rounded card and do not need that effect.

Popup creation now identifies ToolTip content on the UI thread and sends a material-blur flag to the worker before replaying its positioner. Tooltip surfaces skip the menu blur request, including deferred attachment to an unmapped parent. Other popup content keeps its existing blur behavior. The card styling, spacing, placement and rounded border are preserved.

Changes are in the project-owned Avalonia fork under `external/Avalonia` (PopupImpl, PopupImpl.Sink, IWXdgPopup and WXdgPopup). The root checkout and fork both require retaining these working-tree edits.

Thirteen focused GUI checks pass. Four rendered-tooltip cases verify fully transparent corner pixels and opaque centers in light/dark themes at 1x and 1.25x scaling, and verify that the backend policy exempts tooltips while retaining menu blur. The remaining cases cover menu theme changes and Quick Settings. Headless tests use overlay hosts; they verify card pixels and backend classification, not physical compositor output.

Files, Notes and Settings rebuilt with zero warnings/errors. Native shell reload completed with KWin preserved (1042) and the new shell running (22595). Strict compositor preflight returned zero failures/warnings. Whitespace checks pass in both the root checkout and Avalonia fork. Existing system-app processes must be reopened once to load the updated Wayland backend; physical corner appearance remains a user acceptance check.
