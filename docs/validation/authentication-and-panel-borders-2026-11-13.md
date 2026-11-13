# Authentication and border preferences — 2026-11-13

The PolicyKit dialog now uses a compact themed card without ordinary window
chrome, the shared rounded SearchBar input, Small Secondary Cancel and Small
Primary Submit actions, and a Small Ghost Details toggle. The password textbox
is disconnected from SearchBar's query binding so the wrapper does not retain
passwords. Closing clears the response and cancels an active conversation once.
PAM retries, echo prompts, error messages and final failures remain supported.

The current user is automatically selected when included in PolicyKit's offered
identities. A sole alternative administrator is explicitly identified; ambiguous
alternatives cancel rather than choosing an arbitrary account. PolicyKit/PAM
still determine permitted identities and check the password.

Settings → Appearance now exposes “Disable panel borders” (off by default).
It persists through the shell-owned settings IPC and appearance store and
updates shared decorative edge resources in running DaisyOS applications.
The KWin decoration, overview and frame-effect exports remove their matching
edges too. Turning it off restores the active theme. Layout and focus rings are
preserved; applications' independently drawn content borders are outside this
preference.

The experimental GTK caption override was removed at the user's request.
Both GTK user stylesheets retain unrelated rules; DaisyOS's import and generated
caption assets were removed. Default GTK/browser controls are used. Existing
processes require a full quit/reopen to discard previously loaded CSS.
Firefox and Zen's installed browser styles confirm their integrated titlebars
paint their own controls, rather than using GTK headerbar CSS.

Focused tests cover identity selection, password focus/masking/clearing,
submission/cancellation, small action variants, light/dark rendering, preference
persistence, restoring themed edges, preserving focus indicators, native edge
exports and the Appearance switch's layout. Headless dialog captures are at
`/tmp/daisy-authentication-preview/{dark,light}.png`. Logs are retained under
`artifacts/authentication-refresh-2026-11-13/`.

Final verification: 17 system/portal/native-export tests and 7 headless
UI/policy tests passed. Release shell PID 41777 replaced PID 9769; KWin remained
PID 3148. The Release desktop-services backend was restarted and reported ready.
A live settings-IPC round trip persisted the opt-out and exported native
WindowBorder=#00000000 and ActiveOutlineAlpha=0. Restoring the original false
preference restored WindowBorder=#26FFFFFF and ActiveOutlineAlpha=38.
`git diff --check` passed.

## Borderless panel geometry follow-up

The initial opt-out hid strokes but retained their thickness, surface inset and
smaller nested corner radii. This left a transparent strip around translucent
widget and panel surfaces. The preference now also zeros decorative frame
thickness/padding/inset and aligns the nested surface radii with the outer frame.
Only decorative frame resources are affected; input/focus outlines remain.
Custom cartouche fills expand to the perimeter and observe edge resources so
existing popovers redraw when the preference changes.

Eight focused headless tests passed, covering a live preference change on an
existing widget, actual edge pixels, restoring normal frame geometry, media
widget layouts and integer/fractional-scale frame rendering. The rendered
borderless fixture is `/tmp/daisy-borderless-widget.png`; test output is
`/tmp/daisy-panel-border-test.log`.
