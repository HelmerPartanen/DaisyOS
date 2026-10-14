#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
theme_file="$repo_root/src/DaisyOS.Shell/Themes/DaisyTheme.axaml"

test -f "$theme_file"

for obsolete in OSTheme.axaml ConsoleTheme.axaml DesignTokens.axaml DynamicColors.axaml; do
  if test -e "$repo_root/src/DaisyOS.Shell/Themes/$obsolete"; then
    echo "obsolete theme dictionary remains: $obsolete" >&2
    exit 1
  fi
done

if (cd "$repo_root" && rg -n --glob '*.axaml' --glob '!src/DaisyOS.Shell/Themes/DaisyTheme.axaml' '#[0-9A-Fa-f]{6,8}' src/DaisyOS.Shell); then
  echo "rendered XAML colors must be semantic resources from DaisyTheme.axaml" >&2
  exit 1
fi

for key in SurfaceCanvasBrush SurfaceBaseBrush SurfaceRaisedBrush SurfaceOverlayBrush SurfaceSunkenBrush ContentPrimaryBrush ContentSecondaryBrush ContentTertiaryBrush ContentDisabledBrush StrokeSubtleBrush StrokeDefaultBrush StrokeStrongBrush StateHoverBrush StatePressedBrush StateSelectedBrush OverlayScrimBrush ActionPrimaryBrush ActionSecondaryBrush FocusRingBrush StatusSuccessBrush StatusWarningBrush StatusDangerBrush StatusInfoBrush; do
  if ! rg -q "x:Key=\"$key\"" "$theme_file"; then
    echo "missing canonical theme resource: $key" >&2
    exit 1
  fi
done
