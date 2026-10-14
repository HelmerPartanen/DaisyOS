# Accent swatches and Quick Settings — 2026-10-07

Accent swatches explicitly set their minimum width and height to 24px and use a 12px radius. The shared 32px minimum height had stretched them into upright capsules. Hover/press presenters now bind the original button background rather than allowing the platform theme to replace it. Opacity gives a quiet interaction response while the selected outline remains intact.

Quick Settings single toggles, both halves of split toggles, and their containing grids share QuickSettingsControlHeight with the volume container. The default is 44px; TypographyPolicy grows it with the measured standard control height plus 12px for larger fonts. Generic desktop action height remains governed by the standard token.

Ten focused headless GUI checks passed: AccentSwatchTests (both themes and all nine options), QuickSettingsTests, and ControlPolishTests. The swatch tests inject actual pointer movement and verify rendered control geometry and presenter colors after hover transitions. Existing button press-label geometry checks pass.

Task backups are in /tmp/daisy-swatch-quick-before; no-index whitespace checks pass. Git HEAD remains corrupt, and the previously recorded complete solution designer prerequisite is unchanged. Existing Settings processes must be reopened to load their new executable; user application windows are preserved during shell reload.

Native builds and shell reload completed successfully; KWin and open applications stayed running. Strict compositor preflight passes with zero failures and warnings. Physical UI acceptance remains with the user.
