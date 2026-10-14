# DaisyOS Shell Resource Report — 2026-07-13

This report preserves the pre-optimization baseline supplied from the built-in
Shell Resource Diagnostics overlay. It should remain in the repository so later
changes can be compared against the same measurements.

## Baseline capture

- Capture time: 2026-07-13 00:22 local time
- Process uptime: 32 seconds
- Logical processors: 8
- Shell state: desktop running with Settings visible and diagnostics open
- Build configuration: not recorded in the screenshot
- Display configuration: not recorded in the screenshot

| Metric | Baseline |
|---|---:|
| CPU | 1.5% of total 8-core capacity |
| Working set | 976.6 MiB |
| Private memory | 876.2 MiB |
| Managed heap | 219.1 MiB |
| Committed GC memory | 273.4 MiB |
| Threads | 20 |
| Handles | 194 |
| GC collections | 80 / 40 / 6 |
| Backdrop textures | 1 |
| Estimated backdrop texture memory | 225.0 KiB |
| Backdrop generations | 1 |
| Last backdrop build | 18.3 ms |
| Visual instances | 2,889 |

### Most numerous visual types

| Type | Instances |
|---|---:|
| Border | 489 |
| ContentPresenter | 475 |
| TextBlock | 470 |
| StackPanel | 462 |
| Image | 457 |
| TextBox | 450 |
| Grid | 24 |
| Button | 14 |
| ContentControl | 8 |
| Canvas | 4 |

## Baseline assessment

CPU, thread count, handle count, and backdrop cache size were acceptable for a
development build. Memory and visual-tree size were not. The nearly identical
counts of desktop-cell Borders, StackPanels, Images, TextBlocks, and TextBoxes
showed that every empty desktop grid slot created the complete icon-and-rename
template. Invisible controls still consume managed objects, bindings, styles,
layout participation, and native rendering resources.

The first optimization target is therefore desktop-cell materialization. Empty
cells must remain valid selection and drop targets, but should contain only the
lightweight hit-test surface. Icon, label, image, and rename-editor visuals
should exist only when a cell contains an item.

## Comparison method

Future measurements should record:

1. build configuration and commit;
2. display resolution and scaling;
3. exact shell state and open windows;
4. values at 30 seconds and again after five idle minutes;
5. diagnostics closed for the idle CPU sample, then opened for one snapshot;
6. visual counts with the same windows open as the baseline.

Do not compare Debug `dotnet run` numbers directly with a self-contained Release
publish. Runtime mode, display size, wallpaper dimensions, and open Settings
pages materially affect the result.

## Post-optimization results

The following automated capture used real services, the same Debug build family,
the same machine and display session, and a 34-second uptime. Settings was not
open, so this is a strong regression indicator rather than a perfectly matched
UI-state comparison.

| Metric | Baseline | After | Change |
|---|---:|---:|---:|
| Working set | 976.6 MiB | 664.5 MiB | -312.1 MiB (-32.0%) |
| Private memory | 876.2 MiB | 653.6 MiB | -222.6 MiB (-25.4%) |
| Managed heap | 219.1 MiB | 60.8 MiB | -158.3 MiB (-72.2%) |
| Committed GC memory | 273.4 MiB | 75.3 MiB | -198.1 MiB (-72.5%) |
| GC collections | 80 / 40 / 6 | 19 / 9 / 3 | substantially lower startup allocation |
| Visual instances | 2,889 | 1,087 | -1,802 (-62.4%) |
| Images | 457 | 7 | -450 (-98.5%) |

The after-capture visual tree contained 474 `ContentPresenter` instances and 450
lightweight `DesktopGridCellControl` instances. Empty cells no longer contain
icon, label, StackPanel, or TextBox visuals. A full desktop grid remains present
to preserve exact empty-cell outlines, selection geometry, drag targets, and
drop behavior.

CPU is intentionally excluded from the comparison table. The automated capture
ran alongside the developer's existing shell session and sampled 13–21% of
total eight-core capacity depending on media/service state. The supplied
baseline sampled 1.5% over a different interval. A clean single-session Release
benchmark is required before drawing a CPU conclusion.

### Implemented changes

- Desktop item visuals are materialized only for occupied cells.
- Empty cells retain one lightweight Border-derived control for pixel-identical
  outlines, selection, hit testing, and drag/drop.
- Wallpaper transfer now copies decoded pixels directly from Skia to Avalonia,
  removing PNG encoding, byte-array duplication, and PNG decoding at startup.
- The unlocked idle timer returned from one-second polling to a 15-second check;
  one-second cadence is used only while showing an authentication cooldown.
- Diagnostics can export a one-shot text snapshot without opening the overlay.

Capture another comparable snapshot with:

```bash
DAISYOS_RESOURCE_REPORT_DELAY_SECONDS=30 \
  scripts/capture-shell-resources.sh artifacts/diagnostics/shell-resources.txt
```
