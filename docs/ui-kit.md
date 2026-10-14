# DaisyOS UI Kit

The UI Kit is the single visual contract for DaisyOS shell surfaces and applications. Its live catalog is the built-in **UI Kit** application (`daisyos-shell --open-app=uikit`).

## Where to edit components

- `src/DaisyOS.Shell/Themes/DaisyTheme.axaml` owns semantic color, material, radius, spacing, and typography resources.
- `src/DaisyOS.Shell/Styles/ComponentStyles.axaml` owns shared control variants, interaction states, cards, badges, and text roles.
- `src/DaisyOS.Shell/Controls/` owns controls that need structure or behavior, such as `SearchBar`, `ShellListItem`, `QuickSettingTile`, `QuickSettingsSlider`, `SectionHeader`, and `DaisyIcon`.
- Feature views compose these contracts. They should not copy a shared control template or introduce a feature-specific color for an existing semantic role.

## Button contract

Choose one semantic class:

```xml
<Button Classes="Primary" Content="Save" />
<Button Classes="Subtle" Content="Cancel" />
<Button Classes="Ghost" Content="More" />
<Button Classes="Danger" Content="Delete" />
```

- `Primary`: the single preferred action in a region.
- `Subtle`: visible secondary action.
- `Ghost`: toolbar, navigation, and low-emphasis action.
- `Danger`: destructive action.
- Add `IconButton` alongside a semantic class for square icon-only buttons.

Legacy `ShellButton` and `SubtleButton` classes remain compatibility aliases for specialized shell controls. New application UI should use the semantic names above.

## Shared surfaces and text

Use `Border` classes `Card`, `Card Inset`, and `Badge`. Use `TextBlock` classes `Display`, `Heading`, `Title`, `Body`, `Caption`, and `Muted`. Their values intentionally resolve through dynamic theme resources so light/dark and wallpaper-derived themes update every consumer together.

## Contribution rule

If a visual or interaction pattern appears twice, add it to `ComponentStyles.axaml` or create a focused control under `Controls/`, demonstrate all relevant states in the UI Kit app, and consume it from feature views. Do not paste the template into each app.
