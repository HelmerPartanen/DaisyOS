# Application toolbars

Application chrome shares `src/DaisyOS.Shell/Styles/AppToolbarStyles.axaml`, loaded by `ComponentStyles.axaml`. Change toolbar material, padding, minimum height, icon size, or interaction states there instead of adding app-local copies.

```xml
<Border Classes="AppToolbar" HorizontalAlignment="Left">
  <StackPanel Orientation="Horizontal" Spacing="4">
    <Button Classes="IconButton" ToolTip.Tip="{DynamicResource Loc_...}"
            AutomationProperties.Name="{DynamicResource Loc_...}" Command="{Binding BackCommand}">
      <controls:DaisyIcon Icon="arrow-left" Size="16" />
    </Button>
    <Button Classes="Subtle ToolbarAction" Content="{DynamicResource Loc_...}" />
  </StackPanel>
</Border>
```

`AppToolbar` works on a Border containing any layout, including a breadcrumb ItemsControl. Its corner radius follows half its arranged height, including when wrapping increases the height. Independent groups can share a row: Files uses navigation, breadcrumbs, and search/view groups. Keep responsive layout and overflow decisions in the app; shared styles own the appearance.

Use `IconButton` on Buttons and ToggleButtons containing only an icon. The shared template supplies circular hover, press, checked/Active, disabled, and keyboard-focus feedback. Set only Height for a different size; Width follows it. `Small` uses the compact toolbar size; toolbar descendants use that size automatically. Do not add app-local radius, width, template, or press-transform overrides. Text-and-icon actions use `ToolbarAction`, not `IconButton`. Window caption controls keep their dedicated window-management styling.

The default tokens are `AppToolbarMinHeight` (40), `AppToolbarPadding` (4), and `AppToolbarIconSize` (32). Material and feedback colors come from the existing semantic theme resources. Corner radii use `HalfHeightCornerRadiusConverter`; they are derived from actual geometry rather than a fixed radius token. Theme changes retain the same button template instead of switching between custom and Fluent presenter surfaces.

Notes keeps text style, bold, italic, underline, image insertion, and More in its primary toolbar. More contains named font controls and less-used formatting. Its composed flyout uses the shared context-menu frame and material, with full-width action rows and grouped font settings. Toolbar selectors use a pill radius derived from their height; scroll arrows occupy space only when visible. Opening it retains the editor selection. Font-size stepping stays open for repeated adjustments; applying another format returns focus to the editor. Switching documents closes the old menu.

When adding or changing a toolbar, verify compact and wide layouts, light and dark themes, keyboard navigation, disabled and toggled actions, and retained editor selection. Run `AppToolbarTests` and the affected app's UI tests.
