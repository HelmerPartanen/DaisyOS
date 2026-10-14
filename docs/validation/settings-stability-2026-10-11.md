# Settings stability, swatch bounds and close glyphs

## Changes

Accent swatches reserve a 32px button/cell containing a centered 24px fill and a 32px selection ring with a 2px stroke. This leaves a 2px gap without drawing outside the button's clipping bounds.

Settings no longer reconstructs backend pages every 15 seconds. Pages read fresh state when opened and retain their existing explicit action/refresh paths. Night Light applies in place and disables only its toggle while awaiting the backend. Font enumeration is deferred until Appearance opens, and image/resource wallpaper decoding runs outside the UI thread.

Night Light previously started KWin's temporary preview, wrote configuration without notifying/reloading the compositor, then rebuilt Displays using the `running` property. The service now writes persistent Active/temperature/Constant mode state, notifies and reconfigures KWin, clears any old preview, and verifies the `enabled` property. Displays initializes from that property and retains the same toggle/page after applying; failure reads back the actual state and reports the failure. The installed KWin 6.7.5 schema defines Constant as mode zero.

Close glyphs bind directly to their button foreground and use the dedicated white `CaptionCloseGlyphBrush` on hover/press. Both button and glyph are exempt from the shared theme-color transitions, which otherwise delay the white symbol during the red hover background. Files and the shared titlebar consume this behavior; Calculator/Calendar custom titlebar components also use it. Calculator and Calendar's current normal windows use compositor decorations, so their custom components were tested in dedicated hosts.

## Verification

- Accent swatch tests: two light/dark cases passed, including full selection-ring bounds within the button, the 2px gap, selected fill and hovered fill.
- Caption close hover tests: two light/dark cases passed, covering Settings, Files and hosted Calculator/Calendar titlebars with immediate white glyphs and repeated pointer entry.
- Night Light service tests: two passed, covering persistent mode/configuration/reload/state reads and failure reporting.
- ShellDemo checks use the existing temporary exclusions of unrelated stale picker tests. Night Light tests reuse the freshly built project-reference assemblies. The repository test projects were not changed to exclude tests; the full repository suite was not run.
- Live KWin backend check: enabled Constant mode at 4000 K; verified `enabled=true`, `mode=0`, and target temperature 4000 K. After 16 seconds, enabled remained true and current temperature was 4000 K. Disabling was verified, then original Active/Mode/NightTemperature configuration was restored.
- `git diff --check` passed.

The temporary preview's 15-second timeout is also visible in [KWin's implementation](https://raw.githubusercontent.com/KDE/kwin/master/src/plugins/nightlight/nightlightmanager.cpp). This check additionally used the installed `/usr/share/config.kcfg/nightlightsettings.kcfg` and live D-Bus properties.

Already-open standalone applications keep their loaded controls until reopened. Physical UI performance and visual acceptance remain user verification; the backend check does not measure frame pacing.

Native Files, Notes and Settings builds completed successfully and the shell reloaded. Strict compositor session health reported zero required failures and zero warnings.
