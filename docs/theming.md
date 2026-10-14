# Theming

DaisyOS uses Avalonia's built-in `FluentTheme` at normal density. The Fluent theme is loaded first in `App.axaml`; all DaisyOS resources and styles are loaded afterward so they can override visuals without replacing standard control behavior.

## Style layers

- `OSColors.axaml` contains the light/dark palette, semantic brushes, and elevation resources. Views should use `DynamicResource` brushes rather than literal colors.
- `OSTypography.axaml` defines Display, TitleLarge, Title, Subtitle, Body, BodyStrong, Caption, Secondary, and Tertiary text classes using Inter.
- `OSFluentControls.axaml` applies DaisyOS colors to standard Fluent controls and defines `TextField`, `SearchField`, and shared control states.
- `ButtonStyles.axaml` defines Primary, Secondary, Tertiary, Danger, and Subtle variants while retaining Avalonia's Fluent button template and interaction behavior.
- Component style files contain layout-specific classes for the shell, dock, launcher, settings, Files, and system bar.

Use semantic classes in views:

```xml
<Button Classes="Primary" Content="Save" />
<Button Classes="Secondary" Content="Cancel" />
<Button Classes="Danger" Content="Delete" />
<TextBox Classes="TextField" PlaceholderText="Name" />
<TextBlock Classes="Title" Text="Appearance" />
```

The search box remains a lightweight composition of standard `TextBox` and `Button` controls because it supplies a reusable search icon, clear action, and focus API. `SystemBarMenuButton` remains a wrapper around a standard Button and Flyout because it coordinates flyout animation state. The original `VolumeSlider` is retained for its five-step snapping, tick indicator, and established DaisyOS interaction design. Other styling-only custom renderers have been removed.

Future work still includes a high-contrast theme, configurable accent colors, and wallpaper-derived colors. Do not copy proprietary platform assets.
