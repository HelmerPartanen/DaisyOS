# GUI motion

The shared motion language uses a fast start and soft landing: 280ms entrance,
140ms dismissal. The launcher uses the same 220ms entry timing and fast-start curve as systembar panels;
other shell slides use dedicated 220ms entrance and 140ms exit tokens, with a decelerating entrance and accelerating departure. Other content
uses 4–8px interior displacement, and a heavily damped 2% press
response. Colors use the existing 100ms transitions. Motion emphasizes an action
or a change of context; there are no idle loops, hover magnification, or delays
before an action takes effect.

`GuiMotion` owns an interruptible animation per content control. Reopening a
closing panel cancels the dismissal and starts from the current visual state.
Reversals retain the sampled transform and opacity and shorten duration to the
remaining distance. On first reveal the launcher lays out and prepares its static border images while
transparent, initializes a fully
off-screen transform, then becomes visible. It slides opaque through the bottom
clip edge rather than fading into its resting position. Reversals keep the
visible host intact. Launcher appearance does not wait for catalog icon decoding;
existing letter tiles remain visible until icons arrive. Logical visibility changes immediately, so dismissal releases Wayland input
and keyboard capture before the visual exit completes. Old completions cannot
hide a reopened panel. Popup contents cancel their animation on detachment.
Settings live-status refreshes do not replay navigation motion. Files reveals a
new directory or tab once its session is ready; background directory updates do
not replay it. Notes fades a newly selected document without translation, keeping
caret, input-method, and formatting-toolbar coordinates stable while typing.

## KWin boundary

KWin owns application opening, closing, minimizing, maximizing, overview, and
window positioning. DaisyOS does not animate native window geometry, recreate
layer-shell windows, or animate a glass material frame independently of its
blur region. These operations can desynchronize the client pixels, input,
rounded mask, and compositor blur.

The launcher, Quick Settings, calendar, notification drawer, and menu popups
animate only their interior content. Their glass, double borders, layout size,
Wayland role, and blur bounds stay fixed. Their material remains visible until
exit completion. Switching flyout types dismisses the previous card immediately
so two opaque frames do not overlap. Screenshot and lock dismissal is immediate.

Reduce Motion zeros all shared durations and settles active content animations.
Existing physical desktop drag interactions remain under DesktopMotionSettings.

References: [Avalonia animations](https://docs.avaloniaui.net/docs/graphics-animation/animations),
[Avalonia transforms](https://docs.avaloniaui.net/docs/graphics-animation/transforms),
[KWin effects](https://develop.kde.org/docs/plasma/kwineffect/).

## Verification

Headless tests cover interrupted open/close, early input release, stationary
host bounds, zero-duration behavior, detached flyout routing, and existing
Settings layouts. Native acceptance must exercise launcher, each system flyout,
rapid reversal, menu dismissal, overview, and Reduce Motion on the running KWin
session; a build or still screenshot does not establish frame pacing or blur
correctness during movement.

## Notifications

Toast material uses the same XXL frame, inner border, surface color, and shadow
as Quick Settings, Calendar, and the notification drawer. Toast entrance and
exit animate only the shared card contents; native windows and rounded blur
regions remain stationary. The final dismissal releases input before its visual
exit completes. New arrivals append below visible cards so their glass frames do
not move during entrance. The queue respects both three-card capacity and the
native surface's available height. Each card receives five seconds after its
entrance; sender timeouts and urgency do not shorten or extend the toast, and
there is no countdown bar. History remains until dismissed or recalled.

Application identity and its display name are retained separately. App icons
remain visible beside an optional image preview. D-Bus replacements update the
existing card without replaying its entrance, and notification actions and
closure emit the corresponding freedesktop signals.
