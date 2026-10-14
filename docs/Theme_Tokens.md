# DaisyOS design tokens

`src/DaisyOS.Shell/Themes/DaisyTheme.axaml` is the single runtime source of truth. Feature views consume semantic resources with `DynamicResource`; they do not introduce paint, spacing, radius, type-size, target-size, shadow, or duration values.

## Color

| Group | Tokens | Rationale |
|---|---|---|
| Surfaces | `SurfaceCanvas`, `SurfaceBase`, `SurfaceRaised`, `SurfaceOverlay`, `SurfaceSunken`, `SurfaceInput` | Six roles describe depth and input affordance without feature-specific colors. |
| Content | `ContentPrimary`, `ContentSecondary`, `ContentTertiary`, `ContentDisabled`, `ContentOnAction` | Text and icons choose importance and context rather than literal neutral colors. |
| Borders | `StrokeSubtle`, `StrokeDefault`, `StrokeStrong`, `ShellFrameOuterEdge`, `ShellFrameInnerHighlight` | Three ordinary stroke strengths plus two glass-frame roles cover separators, controls, and shell materials. |
| Actions | `ActionPrimary`, `ActionPrimaryHover`, `ActionPrimaryPressed`, `ActionSecondary`, `ActionSecondaryHover`, `ActionSecondaryPressed` | Every action state derives from one semantic hierarchy. |
| Feedback | `StatusSuccess`, `StatusWarning`, `StatusDanger`, `StatusInfo` and matching `Container` brushes | Foreground/container pairs communicate meaning and meet WCAG AA for normal text in both themes. |
| Interaction | `StateHover`, `StatePressed`, `StateSelected`, `FocusRing`, `OverlayScrim` | Interaction feedback remains consistent regardless of component type. |

The default primary action uses `#0067C0` with white content in both themes (5.67:1). Light status pairs range from 5.58:1 to 6.05:1; dark status pairs range from 5.49:1 to 7.18:1. Wallpaper swatches and file-category colors are content data, but still live in the theme dictionary as named resources.

## Typography

| Role | Size / line height | Weight | Use |
|---|---:|---|---|
| Caption | 12 / 16 | Regular or Medium | Metadata and secondary labels. |
| Body small | 13 / 18 | Regular or SemiBold | Compact controls and dense rows. |
| Body | 14 / 20 | Regular | Default reading and form content. |
| Body large | 16 / 24 | Regular or SemiBold | Emphasized body content. |
| Title | 20 / 28 | SemiBold | Card, dialog, and page-section titles. |
| Heading | 24 / 32 | SemiBold | Page headings. |
| Display | 32 / 40 | Bold | Page-level metrics and large labels. |
| Display large | 44 / 52 | SemiBold or Bold | Calculator output and console/widget hero values. |
| Display hero | 104 / 112 | Bold | Lock-screen clock only. |

`ShellFontFamily` is the only application font family. The core seven-role scale prevents one-off sizes and supplies explicit line heights for stable rhythm; two named display sizes cover the calculator/console and lock-screen clock without leaking literals into feature views.

## Spacing

`Space0`, `Space1`, `Space2`, `Space3`, `Space4`, `Space5`, `Space6`, `Space8`, `Space10`, `Space12`, and `Space16` equal 0, 4, 8, 12, 16, 20, 24, 32, 40, 48, and 64px. The 4px grid is compact enough for desktop density and predictable enough for visual alignment.

Semantic inset tokens cover controls, cards, normal panels, and compact panels. Directional spacing is composed from the same scale; it does not create a new arbitrary measurement.

## Radius and borders

| Token | Value | Use |
|---|---:|---|
| `RadiusControl` | 10 | Buttons, inputs, tabs, and list actions. |
| `RadiusControlInner` | 8 | Nested content inside a control. |
| `RadiusSurface` / `RadiusDialog` | 18 | Cards, flyouts, dialogs, and glass panels. |
| `RadiusSurfaceLeft` / `RadiusSurfaceRight` | 18 / 0 | Split surface controls: 18px outer corners and square shared edges. |
| `RadiusSurfaceInner` | 16 | Nested surface following an 18px parent. |
| `RadiusPill` / `RadiusCircle` | 999 | Geometry-independent pills and circular targets. |
| `BorderThicknessDefault` | 1 | Ordinary separation. |
| `BorderThicknessFocus` | 2 | Visible keyboard focus and validation error. |

App-window radii remain separately named because KWin consumes matching scalar values. Nested radii decrease with the inset instead of repeating the outer radius.

## Elevation and material

Cards use a border before a shadow. Windows, flyouts, popups, panels, dialogs, and shell chrome use the existing named `Elevation*` levels in increasing order. Glass materials use `SurfaceBaseBrush` plus shell frame strokes; content surfaces use opaque raised/sunken roles. Blur is reserved for compositor-backed shell material and never substitutes for contrast.

## Motion

| Token | Duration | Use |
|---|---:|---|
| `MotionDurationShellEnter` | 220ms | Shell bars, flyouts and toast entrance. |
| `MotionDurationShellExit` | 140ms | Shell slide dismissal. |
| `MotionDurationEnter` | 280ms | Interior page and popover entrance. |
| `MotionDurationExit` | 140ms | Interior dismissal with immediate input release. |
| `MotionDelayBrief` | 80ms | Brief interior stagger, disabled by Reduce Motion. |
| `MotionDurationInstant` | 0ms | Immediate state synchronization. |
| `MotionDurationHover` | 50ms | Pointer and row background feedback. |
| `MotionDurationFast` | 100ms | Press and small feedback. |
| `MotionDurationBase` | 160ms | Focus, selection, and shared theme color/image changes. |
| `MotionDurationSlow` | 220ms | Enter, exit, and larger spatial changes. |

Launcher and systembar panel entrances use `MotionEaseEnter` (0.22, 1, 0.36, 1); exits use `MotionEaseExit` (0.4, 0, 0.7, 0.2). Pressure feedback uses the heavily damped `MotionEasePress` spring. See [GUI motion](gui-motion.md) for native-surface constraints. New motion changes only opacity or transforms, remains interruptible, and never blocks input. Reduce Motion replaces all shared duration resources with zero at runtime. Rendered XAML transitions consume these resources, including legacy desktop widgets.

The shared `ThemeTransitionPolicy` adds missing brush and color transitions to controls, preserving explicit interaction transitions. `DaisyIcon.CurrentColor` follows the foreground animation directly rather than adding a second animation. Themed image sources cross-fade at animation priority, preserving their original resource/data bindings. Color and image transitions use `MotionDurationBase`, including its zero-duration reduced-motion value.

## Sizing and hit targets

| Token | Value | Use |
|---|---:|---|
| `HitTargetCompact` / `ControlHeightCompact` | 32 | Compatibility alias for standard desktop controls. |
| `HitTargetStandard` / `ControlHeightStandard` | 32 | Default desktop buttons, inputs, rows, and navigation. |
| `HitTargetComfortable` / `ControlHeightComfortable` | 32 | Legacy alias for standard desktop controls. |
| `SearchHeightStandard` | 32 | Toolbars and application search. |
| `SearchHeightProminent` | 32 | Compatibility alias for launcher and Quick Search. |
| `IconSizeSmall/Standard/Medium/Large/Hero` | 12/16/20/24/32 | One optical icon scale for HugeIcons. |
| `ReadableContentMaxWidth` | 820 | Settings and long-form reading width. |
| `TaskbarMaximumWidth` | 1200 | Maximum dock width in logical pixels, further limited by screen width with 12px side clearance. |

Visual glyphs may be smaller than their target. Interactive targets normally stay at least 32px on desktop and use 44px where touch is expected. Crowded Files tabs and taskbar app slots shrink to keep all items in one row; their tooltips and keyboard actions remain available.

## Migration rule

Shared components migrate first, then shell flyouts, then applications. A feature may retain a literal only when it represents user content, media dimensions, or protocol geometry rather than design; the exception must be named and documented. The current enforcement gate rejects undeclared rendered XAML colors. Motion literals are covered by contract tests and repository scans. Phase 3 applies the spacing, radius, typography, and sizing tokens component by component, where each remaining geometry literal can be classified without changing content or protocol dimensions.

## Shared navigation and calendar markers

`SidebarItemGap` is 4px and is shared by Settings navigation, Files location lists,
and the file picker. `CalendarTodayBackgroundBrush` and
`CalendarTodayForegroundBrush` form an opaque black/white pair in light mode
and a white/black pair in dark mode. Both the shell calendar and desktop calendar
widget use the pair so today's marker stays visible over every material.

Custom switches use `Themes/ToggleSwitchTheme.axaml`. The track is 56×24 with a
28×20 capsule thumb and 24px travel, with a 2px inset at either end; the control retains a minimum 32px hit target.
`ToggleTrackOffBrush` adapts to light/dark, the checked track uses the selected
accent, and the pale thumb and white outline remain stable in both states.
The thumb and track share a 100ms cubic ease-in-out transition. Control-level
transition resources respect Reduce motion; transition durations do not use
dynamic bindings on nested animation objects.


## User-selected fonts and language

`TypographyPolicy` is the runtime authority for type sizes and line heights after
loading user preferences. All sizes are whole pixels; natural HarfBuzz/Skia line
metrics can increase the minimum line height. `ControlHeightStandard`, compact/
comfortable aliases, hit targets and search heights share one 4px-grid height:
32px for normal Inter, increasing together when larger type needs it.
`ShellChromeHeight` follows that control height. Consumers must use dynamic
resources rather than cache a numeric height or a font size.

The shared text rendering policy uses grayscale antialiasing, strong hinting,
aligned baselines and layout rounding, including native `PopupRoot` hosts for
dropdowns and tooltips. Display DPI is supplied by the Wayland surface; keep
font sizes in logical units and rasterize at the current render scale rather
than stretching a cached low-resolution text image. The chosen system family retains priority,
with Noto/DejaVu and platform character fallback for additional scripts. Language
labels use the `Loc_` resource catalog; user-entered text and backend identifiers
are not catalog keys. See `architecture/languages-and-typography.md` for the
research, translation workflow and locale boundaries.

Quick Settings tile controls and the volume container use `QuickSettingsControlHeight`: 44px at normal typography, growing with the measured standard control height plus 12px. Accent color swatches are circular 24px controls and explicitly override the standard minimum height.

Input sizing follow-up: `InputHeightStandard`, `SearchHeightStandard` and `SearchHeightProminent` are 40px at normal typography, growing with the standard measured control height plus 8px. Single-line text fields and dropdowns use the input token, including dynamically generated Settings pickers. Search keeps its centered inner content and gains space in its outer hit surface.

System button variants use `Small`, `Medium`, and `Large` classes, with `ButtonHeightSmall` (32px), `ButtonHeightMedium` (36px), and `ButtonHeightLarge` (42px). Large follows the existing Files sidebar reference exactly; Small follows widget library Done. TypographyPolicy grows all three with measured font height. Settings, Notes and Files sidebar rows share Large; pinned Files containers follow the same height. Use the size class alongside the action/material class, for example `Classes="Primary Medium"`.
Filled destructive actions use `ActionDangerBrush`, `ActionDangerHoverBrush`,
and `ActionDangerPressedBrush`, with `ContentOnActionBrush` for white content.
Keep `StatusDangerBrush` for error text and destructive menu labels. Native close
buttons export the filled-action hover role rather than using the lighter text color.
Translated action buttons use content-measured widths with `MinWidth` when a
minimum hit area is needed. Avoid fixed button/group widths for translated labels;
toolbar groups must measure their children and leave flexible navigation space.

`MotionDurationHover` is 50 ms for pointer/row background feedback and becomes zero with Reduce motion. It is separate from `MotionDurationBase` (160 ms) used by theme color fades. Files rows animate their presenter fill only, avoiding stacked parent/presenter hover trails.

`CaptionCloseGlyphBrush` is always white, independent of accent contrast, for custom titlebar close symbols on hover and press.
