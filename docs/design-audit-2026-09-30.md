# DaisyOS product design audit — Phase 1

Date: 2026-09-30

This audit maps the current product before token or component changes. Step counts use the fastest discoverable path from an idle desktop. One shortcut chord, click, selection, or submitted text entry counts as one step. Waiting is recorded as friction rather than a step.

## Product surface map

### Desktop and shell

- Desktop: wallpaper layer, desktop items, rubber-band selection, drag/reorder, folder creation, rename, clipboard actions, context menus, item properties, Quick Look, and widget placement.
- Taskbar/Dock: Launcher, pinned/running apps, window activation/minimize behavior, taskbar item context menus, drag reorder, and Dock placement.
- Launcher: search, top result, application grid, app context menu, pin/unpin, uninstall, and power/session menu.
- System Bar: status cluster, volume, network, Bluetooth, battery, clock, and entry points to Quick Settings, Notifications, and Calendar.
- Quick Settings: main controls, volume and brightness sliders, audio-output page, Wi-Fi network page, Bluetooth-device page, Night Light, airplane mode, and Settings handoff.
- Notifications: notification list, empty state, per-item actions/dismissal, clear-all, Do Not Disturb, toast host, and transient feedback host.
- Calendar flyout: month navigation, date selection, event list, and Calendar-app handoff.
- Clipboard History: recent clipboard items, copy/activate, empty state, and clear.
- Screenshot: screen/window/region modes, selection guides, capture controls, flash feedback, cancel, and result handling.
- Lock screen: clock/status, unlock prompt, password failure, session entry, and power controls.
- Quick Look: image, text, folder/file metadata, unsupported/error presentation, and dismiss.
- LocalSend overlay: selected-file preview, device discovery, no-device state, selection, send, cancel, incoming accept/decline, and reveal-in-Files.
- Window management: app activation, minimize/restore, close, fullscreen, Alt+Tab switching, overview, unresponsive-app warning, and force quit.
- Desktop widgets: library, Clock, Digital Clock, Calendar, Media, Photos/Image, size selection, placement, configuration, and widget context menus.
- Console mode: home/game library, header/action bars, media widget, quick menu, current-game actions, and Settings pages for audio, network, Bluetooth, display, controls, and system/performance.
- Diagnostics: shell metrics panel, per-app performance overlay, resource diagnostics, and Task Manager.

### Built-in applications and dialogs

- Settings: System overview; Power, Multitasking, Performance, Recovery, and Clipboard details; Displays; Sound; Network; Bluetooth; Appearance; Notifications; Apps and Installed Apps; Storage; Accessibility; Keyboard and mouse; Date and time; About; navigation search; display-change confirmation; and Wi-Fi credential dialog.
- Files: sidebar locations and pinned folders, tabs, breadcrumbs/path entry, grid/list views, selection, search, sort, hidden files, create, rename, open, copy/cut/paste, Trash/undo, permanent delete confirmation, archive/extract, share, properties, terminal/browser handoff, drag/drop, previews, and file/folder picker.
- Notes: note list, add, duplicate, search, edit, autosave/manual save, pin, external-file open, delete confirmation, save failure/retry, and unsaved-close protection.
- Calendar: month/day navigation, event list, create/edit/delete event, and empty day state.
- Calculator: standard calculator input, keyboard input, history, memory, and error/result states.
- App Store: category navigation, featured apps, search, loading, empty, error/retry, install, open, uninstall, and status feedback.
- Task Manager: process list, sorting/refresh, process selection, and end-task action.
- UI Kit: live catalog for buttons, inputs, status, feedback, list items, typography, surfaces, spacing, selected navigation, and active search.
- Shared dialogs: confirmation, Wi-Fi password, desktop item properties, file/folder picker, and compositor-owned native menus/flyouts.

### Shared component inventory

- Foundations: `DaisyTheme`, `ComponentStyles`, Fluent overrides, dropdown/menu styles, window-frame styles, typography roles, surface roles, and icon alias/bundle generation.
- Actions and selection: primary, secondary/subtle, ghost, danger, shell, navigation, icon, split, caption, menu, taskbar, calculator, screenshot, and feature-local buttons; ToggleButton, ToggleSwitch, CheckBox, RadioButton, MenuItem, tabs, and list/grid selection.
- Inputs: TextBox, password input, SearchBar, ToolbarSearchPopover, ComboBox/dropdown, Slider, QuickSettingsSlider, ConsolePillSlider, date/time controls, and inline rename.
- Navigation and structure: ShellListItem, SectionHeader, FlyoutPageHeader, BreadcrumbBar, Files tabs, Settings navigation, cards, badges, separators, scroll surfaces, and empty/loading/error hosts.
- Feedback and overlays: toast, notification, progress indicator, retry/error panel, confirmation dialog, context menu, tooltip, focus ring, selection check, AppPerformanceOverlay, and ShellMetricsPanel.
- Window chrome: shared app frame plus separate Settings, Notes, Calendar, and Calculator title-bar implementations; system-app fullscreen controller and native compositor integration.
- Content: DaisyIcon, raster app icons, thumbnails, file visuals, media artwork, wallpaper, widget previews, calendar day cells, process rows, and virtualized file grid.
- Direct manipulation: DesktopItemView, DesktopReorderPanel, drag ghost, file drag/drop, taskbar reorder, widget placement, screenshot selection, and pointer-owned popup positioning.

## Top tasks and current step counts

| Task | Fastest current path | Steps | Friction observed |
|---|---|---:|---|
| Open a known app | `Super` → type name → `Enter` | 3 | Good keyboard path; pointer path depends on app position and Launcher grid state. |
| Switch windows | `Alt+Tab` | 1 | Fast, but DaisyOS-owned and third-party window behavior is split between shell and compositor. |
| Open Files | `Super+E` | 1 | Strong direct shortcut. |
| Find and open a file | `Super+E` → `Ctrl+F` → type query → `Enter` | 4 | Search is folder-scoped; no shell-wide recent/file search path is exposed. |
| Create a folder in Files | `Ctrl+Shift+N` → type name → `Enter` | 3 | Good keyboard path; error recovery depends on inline feedback. |
| Move a file to Trash | select item → `Delete` | 2 | Good reversible default; `Ctrl+Z` provides recovery. |
| Change a known setting | `Super+I` → `Ctrl+F` → type setting → `Enter` → change control | 5 | Search reduces category hunting, but focus/result behavior differs from Launcher search. |
| Connect to secured Wi-Fi | click system cluster → Wi-Fi details → network → type password → Connect | 5 | Common flow requires entering a subpage; unavailable/slow discovery has no universal skeleton pattern. |
| Change volume | click system cluster → adjust slider | 2 | Direct and compact; no documented global volume shortcut. |
| Check notifications | `Super+N` | 1 | Fast; action hierarchy and per-item density differ from toast presentation. |
| Create a note | open Notes via Launcher (3) → `Ctrl+N` → type | 5 | Once open, creation is one command and focus is correct; Notes has no global shortcut. |
| Take a region screenshot | `Super+Shift+S` → drag region → confirm/capture | 3 | Source implements the shortcut, but the public shortcut document still lists screenshot shortcuts as unimplemented. |
| Share a file with LocalSend | select file → context menu → Share → LocalSend → select device → Send | 6 | Nested Share submenu and a second confirmation make this one of the longest common flows. |
| Lock the session | `Super+L` | 1 | Strong direct shortcut; unlock/power states still need full keyboard and live-runtime acceptance. |
| Shut down | `Alt+F4` → Shut down | 2 | Fast from desktop; pointer path through Launcher power is three steps. Destructive/session confirmation policy is not expressed consistently across shell and lock screen. |

No task was redesigned in Phase 1. These counts are the baseline for the Phase 4 before/after comparison.

## Inconsistency inventory

### Token and geometry fragmentation

- Non-theme XAML contains 352 literal margins with 161 distinct values, 184 paddings with 61 values, 286 widths with 83 values, 350 heights with 64 values, 100 corner radii with 32 values, 384 font sizes with 20 values, and 309 spacing values with 21 values.
- There are 143 literal hexadecimal colors across 14 files outside `DaisyTheme`. Some are content data such as wallpaper swatches, but they still need named data/theme tokens under the requested single-source rule.
- There are 651 style selectors across 41 non-theme XAML files. Files, Settings, Launcher, Console Home, Screenshot, LocalSend, Calculator, System Bar, and feature title bars each maintain substantial local component systems.
- The written geometry contract and implementation disagree: the contract describes 40px standard controls, 44px comfortable controls, a 52px prominent search field, and 10px control radius; shared semantic buttons are 32px, SearchBar is 36px, and `RoundedMD` is 12px.
- Fractional geometry remains (`11.5` font size, `3.5` radius, `2.5` border), conflicting with the repository guideline against fractional font sizes and weakening pixel consistency.

### Typography

- Inter is established as the shell family, but most feature views specify local font sizes and weights instead of semantic type roles.
- The current scale contains at least 20 literal sizes, including 10, 11, 11.5, 12, 13, 14, 15, 16, 20, 22, and 32. Line-height is rarely explicit, so equal font sizes can render with different vertical rhythm.
- `Display`, `Heading`, `Title`, `Body`, and `Caption/Muted` exist, but adoption is low compared with hundreds of local `FontSize` declarations.

### Component duplication and state gaps

- Four built-in apps carry separate title-bar controls and local styles while a shared app-frame system also exists.
- App Store, LocalSend, Files, Settings, Launcher, Calculator, Screenshot, widgets, and console surfaces define feature-local button/card/list variants that overlap shared Primary, Secondary, Ghost, Danger, Card, Badge, and navigation roles.
- Shared buttons implement default, hover, pressed, disabled, and focus, but focus uses `:focus` rather than consistently limiting the ring to `:focus-visible`. Loading and selected states are not part of the shared semantic button contract.
- TextBox has default, hover, and focus treatment, but no shared disabled, read-only, error, success, or loading contract. Feature errors are assembled independently.
- SearchBar has a coherent focus overlay but its clear target is 24px, below the stated 32px desktop minimum and 44px touch target.
- ShellListItem is structurally shared but carries feature-specific Wi-Fi, Bluetooth, and output-device classes, coupling one reusable component to unrelated consumers.
- Empty, loading, error, retry, and no-result states exist in several workflows, but their structure, icon, spacing, action placement, and announcement behavior are not shared.
- Destructive actions vary between confirmation, immediate action, undo, and toast feedback. The desired rule is Trash/undo for reversible deletion and confirmation only for irreversible/session actions.

### Color, elevation, and material

- Semantic surface/content/status roles are well started in `DaisyTheme`, including light/dark dictionaries, but literal black/white/status and content colors remain in lock screen, widgets, Settings swatches, file visuals, and runtime theming code.
- Elevation contains many narrowly named shadow tokens without a documented level model, while some local surfaces add their own shadows or omit borders entirely.
- Glass, opaque raised, overlay, input, canvas, and sunken surfaces are available, but feature code chooses among them inconsistently. Similar cards can be borderless, bordered, shadowed, translucent, or opaque.
- HugeIcons is the primary control icon set, but aliases, mixed case names, raster built-in app icons, legacy semantic names, and runtime-selected strings make missing/inconsistent icons possible without bundle tests.

### Motion and perceived speed

- Durations and easings are embedded throughout XAML and C#: observed values include 80, 90, 100, 120, 140, 145, 150, 160, 170, 180, 200, 240, 250, 320, 520, and 800ms.
- The 800ms image transitions exceed the requested 120–250ms range. Lock-screen motion uses 320ms; desktop folder dwell uses 520ms.
- Width is animated in the ToggleSwitch template, which is a layout-affecting property. Most other motion correctly uses opacity or transforms.
- Reduced Motion is described in documentation and Settings, but motion is not routed through shared tokens/behavior, so feature-local transitions can continue independently.
- Async workflows commonly expose progress, but there is no audit evidence that every action changes visual state within 100ms. This needs instrumentation rather than source-level assumption.

### Navigation, accessibility, and documentation drift

- Keyboard coverage is strongest in Files and the shell. App Store, Calendar, Task Manager, widget configuration, LocalSend, and several nested flyout pages have less explicit keyboard handling.
- Automation names/tooltips are present on many icon controls, but coverage is uneven across dynamically created controls, custom rows, widget previews, and feature-local buttons.
- Tab order is mostly implicit. Complex composite surfaces do not define a documented focus order or a shared focus-restoration policy.
- The source implements `Super+V`, screenshot, Task Manager, and terminal shortcuts while the shortcut guide marks some of those bindings as reserved/unimplemented. The product contract and implementation disagree.
- The shared SearchBar, Settings search, Launcher search, Files toolbar search, and App Store search use related visuals but different dismissal, result navigation, retained-query, and focus-restoration rules.
- Responsive rules are documented, but many windows and overlays retain fixed widths/minimums. The 56 XAML surfaces do not all have small/large, long-text, or increased-text verification.

## Prioritized findings

1. **P0 — Establish one enforceable token contract.** Geometry, typography, color, elevation, motion, and sizing are too fragmented to make component polish reliable. Reconcile the existing theme and layout-contract values before migrating views.
2. **P0 — Consolidate shared component states.** Buttons, inputs, list rows, cards, menus, dialogs, feedback states, and title bars need one state matrix and implementation before feature-level visual work.
3. **P0 — Make reduced motion and response feedback systemic.** Central motion tokens, a real reduced-motion switch path, transform/opacity-only transitions, and a <100ms response probe are required.
4. **P1 — Unify search behavior.** Launcher, Settings, Files, picker, Notes, and App Store should share keyboard navigation, Escape behavior, clear affordance, focus restoration, loading, and no-result patterns.
5. **P1 — Normalize shell flyouts and recovery states.** Quick Settings subpages, Notifications, Calendar, clipboard, LocalSend, and native menus need the same header, navigation, spacing, error/retry, and focus contracts.
6. **P1 — Reduce the longest common flows.** Wi-Fi should expose networks directly when appropriate; LocalSend should make the likely device/action direct; known Settings results should open ready to change; Notes deserves a quick-create entry point.
7. **P1 — Consolidate window chrome and responsive behavior.** Replace parallel title bars and local window geometry with shared chrome and explicit compact/standard/spacious states.
8. **P1 — Close accessibility gaps.** Use focus-visible consistently, meet target sizes, define tab order/focus return, cover dynamic controls with automation metadata, and verify contrast rather than inferring it from token names.
9. **P2 — Normalize icons and content visuals.** Keep HugeIcons as the control set, move all runtime aliases through a typed semantic catalog, and define when raster app/content artwork is appropriate.
10. **P2 — Correct product documentation drift.** Shortcut, layout, UI-kit, and runtime behavior must describe the same system and be guarded by contract tests.

## Phase 2 entry criteria

Phase 2 should begin by selecting one canonical density: 32px compact toolbar targets, 40px standard desktop controls, and 44px comfortable/touch controls. The default should be 40px because this is a desktop OS with pointer and keyboard as primary inputs while preserving 44px for touch-heavy and primary actions. This reconciles speed with accessibility and gives the migration a stable target.

Before feature views are changed, Phase 2 must define and document colors/contrast pairs, typography with line heights, a 4px spacing scale, radii, border/elevation levels, blur/material roles, motion/reduced-motion behavior, icon sizes, and control/target sizes in `DaisyTheme` plus one human-readable token reference.
