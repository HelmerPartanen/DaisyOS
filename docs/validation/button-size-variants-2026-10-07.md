# System button size variants — 2026-10-07

The reference widget Done button is 32px and Files navigation is 42px. Shared Small, Medium and Large classes use ButtonHeightSmall/Medium/Large at 32/36/42px. TypographyPolicy derives them from measured standard height with offsets 0/4/10px. Large retains the exact supplied reference rather than rounding it to a different height.

Files, Notes and Settings sidebar buttons use Large. Files pinned row containers and their navigation bodies use the same token. Widget library Done explicitly uses Small. Size classes compose with existing action/material classes, for example Primary Medium; both Buttons and ToggleButtons support them. Existing input and Quick Settings sizing remains governed by its own tokens.

Validation covers distinct rendered variant heights and centered press-transform origins, existing control geometry, widget library layout, Finnish Settings and larger-font rendering. The repository retains its corrupt Git HEAD and previously documented complete solution designer prerequisite.

Focused GUI result: 16 passed, zero failed.
Native builds and shell reload succeeded; strict compositor preflight has zero failures and warnings. Existing Files/Notes/Settings processes should be closed and reopened to load the new sidebar sizing.
