# Widget interaction fixes — 2026-11-04

The gallery arrows and desktop remove buttons now share a dedicated button theme. It replaces Fluent's default presenter state styling and press transform, keeps the circular geometry and shadow stable, and supplies hover, pressed, and keyboard-focus feedback using existing DaisyOS resources. The remove control is no longer clipped by its widget host. Disabled gallery arrows fade without simultaneously replacing their fill and shadow.

Shelf media placement is handled by the owning ShellView through internal request events. The shelf can live in a separate native surface without looking up its owner after an arbitrary delay. The action no longer sets a persistent zero opacity. Desktop placement requests a layout refresh, and the shelf is hidden after the replacement widget has been created and laid out.

Validation: 21 focused headless UI tests passed, including existing widget gallery layout, desktop media, and bottom chrome tests. New regressions exercise real mouse hover/press/release on both circular button types, repeated back/forward navigation, and three shelf/desktop round trips with the shelf reparented into another window. The latter checks the desktop card's visibility and committed cell and verifies that the shelf returns at full opacity.

Command:

```sh
dotnet test tests/DaisyOS.ShellDemo.Tests/DaisyOS.ShellDemo.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~WidgetInteractionRegressionTests|FullyQualifiedName~WidgetLibraryLayoutTests|FullyQualifiedName~DesktopMediaWidgetTests|FullyQualifiedName~BottomChromeTests' -m:2
```

The Release build and whitespace check passed. Physical KWin visual acceptance of the reported flicker remains outstanding; these tests verify rendered-control state and routing, not presented frame pacing on the user's GPU. The running desktop was not restarted.
