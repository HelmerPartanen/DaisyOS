# DaisyOS UI, Layout, and Motion Audit

This document is the implementation contract for DaisyOS-owned shell surfaces. It preserves the glass material and typography while making geometry, placement, responsive behavior, and motion predictable.

## Geometry contract

| Role | Value | Use |
| --- | ---: | --- |
| Compact control | 32 px | Top bar and dense toolbars only |
| Small control | 36 px | Secondary compact actions |
| Standard control | 40 px | Inputs, list actions, and regular buttons |
| Comfortable control | 44 px | Primary touch and pointer targets |
| Search field | 52 px | Quick Search and prominent search surfaces |
| Top bar | 38 px | Fixed shell chrome height |
| Shell edge inset | 12 px | Flyouts and screen-edge content |
| Dock edge inset | 8 px | Every Dock orientation |
| Panel padding | 20 px | Standard glass panels |
| Compact panel padding | 16 px | Constrained windows and onboarding |
| Control radius | 10 px | Buttons and inputs |
| Surface radius | 18 px | Glass panels, flyouts, and Dock |
| Readable content limit | 820 px | Settings and long-form content |

Nested surfaces follow the outer curvature. Inner radii are smaller than the containing surface, and separators are used only where they communicate structure.

## Responsive rules

- Layout is based on logical width and height. Display scaling is handled by Avalonia; the shell is never uniformly shrunk to compensate for a tall display.
- Shell profiles are `Compact` below 1100 logical pixels wide or 700 high, `Spacious` from 2200×1200, and `Standard` otherwise.
- Settings and Files use conventional two-pane layouts. Their sidebars collapse to a 72 px icon rail below 760 logical pixels and restore automatically above that breakpoint.
- Settings content remains centered and no wider than 820 px. Files prioritizes the item surface while preserving navigation and toolbar access.
- Quick Search remains centered on the viewport; results grow downward without moving the input.
- Launcher, Power, and Updates panels are offset from the active Dock edge. Calendar is anchored beneath the clock; Notifications is anchored beneath the system controls.
- Flyouts use viewport-aware maximum sizes and native scrolling. Fixed widths are avoided where they could clip at increased text size or narrow logical widths.
- Invalid or transient dimensions reported during startup do not trigger compact mode.

## Component rules

- Primary targets are 40–44 px. The system bar and dense file toolbar use 32–36 px controls.
- Buttons, inputs, search fields, cards, tabs, menu rows, and list rows use shared geometry resources instead of local measurements.
- Hover, pressed, selected, active, disabled, and keyboard focus are separate states. Keyboard focus uses a visible two-pixel theme-aware outline.
- Labels may wrap where meaning matters and trim in single-line toolbars. Icons, chevrons, thumbnails, badges, and indicators are optically centered.
- Settings and Files retain native scroll behavior and avoid nested scrolling in their primary content regions.
- Empty, loading, error, and no-result states remain centered within the useful content area and use short, plain language.

## Motion contract

| Role | Duration | Treatment |
| --- | ---: | --- |
| Feedback | 100 ms | Hover and press response |
| Selection | 140 ms | Selected-state movement and color |
| Panel enter | 200 ms | Short translation plus fade |
| Panel exit | 140 ms | Short translation plus fade |
| Navigation | 180 ms | Content/view changes |
| Drag | 160 ms | Drag imagery and placement feedback |

Motion uses opacity and transforms instead of layout properties. Panel travel is limited to 12 px and pressed scale to 0.98. Directional shadows and low-opacity treatment are restricted to moving imagery such as drag ghosts and previews; text and whole glass panels are never blurred.

Reduce Motion removes scale, travel, and perceptual trails. State remains clear through immediate changes or short fades, and interrupted transitions must not retain stale transforms, focus, visibility, or hit testing.

## Visual review matrix

The automated suite covers layout profiles, transient dimensions, overlay clamping, shared geometry resources, and motion-duration contracts. Release review must also exercise each row below in light, dark, and high-contrast themes.

| Viewport | Scale checks | Required review |
| --- | --- | --- |
| 1366×768 | 100%, 125% | No clipping; collapsed sidebars remain usable; flyouts stay on-screen |
| 1920×1080 | 100%, 125%, 150% | Comfortable density; bounded Settings content; centered overlays |
| 2560×1440 | 100%, 125%, 150% | No whole-shell enlargement or excessive content width |
| 3840×2160 | 100%, 150%, 200% | Stable alignment, readable targets, and crisp glass borders |

For every viewport, verify long translated text, empty and large lists, keyboard-only navigation, focus restoration after overlays close, every Dock edge, animation interruption, and Reduce Motion. Use the resource diagnostics overlay to confirm rendering and backdrop generation settle after transitions.

Third-party window chrome and compositor-owned effects are outside this contract; DaisyOS-owned windows and shell overlays are covered.
