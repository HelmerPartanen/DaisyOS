# Design-token validation — 2026-09-30

Phase 2 establishes `DaisyTheme.axaml` as the runtime source of truth and documents the contract in `docs/Theme_Tokens.md`.

## Implemented

- Added a 4px spacing scale, seven typography roles with line heights, shared radii and borders, elevation roles, 100/160/220ms motion, 32/40/44px target sizes, and a five-size icon scale.
- Corrected primary-action and semantic foreground/container pairs to meet WCAG AA in light and dark themes.
- Migrated shared buttons, text inputs, cards, search, wallpaper swatches, lock-screen/media overlays, and all rendered XAML animation durations to named resources.
- Wired Reduce Motion to replace every motion duration with zero at runtime.
- Kept keyboard focus tied to `:focus-visible` and to the stable focus-ring role rather than the user accent.

## Automated evidence

- `scripts/check-theme-tokens.sh`: pass; no rendered XAML color literals outside `DaisyTheme.axaml`.
- `dotnet build src/DaisyOS.Shell/DaisyOS.Shell.csproj -f net10.0 --no-restore --disable-build-servers -m:1`: pass, zero warnings and errors.
- `dotnet test tests/DaisyOS.Tests/DaisyOS.Tests.csproj --no-restore --disable-build-servers -m:1`: 338 passed, 1 live screenshot test skipped, 0 failed.
- `git diff --check`: pass.

## Remaining boundary

Phase 3 applies the canonical spacing, radius, typography, and sizing roles to each feature component while distinguishing design values from content dimensions and compositor geometry. Live optical acceptance for light/dark themes and Reduce Motion remains part of the final runtime verification.
