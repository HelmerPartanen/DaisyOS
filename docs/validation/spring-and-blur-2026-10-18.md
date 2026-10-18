# Spring motion and compositor material synchronization

The previous slide clock began before the first rendered frame. Native mapping
or layout delays could consume most of the entrance. Slides now use animation
frame timestamps, start at the first frame, and limit advancement after a stall.
Position uses a lightly underdamped spring with velocity retained on reversal;
opacity continues to use critical damping. Shared spring easing also has a
restrained overshoot instead of a critically damped stop.

The Wayland backend previously committed blur changes immediately against the
old pixel buffer. Blur masks now travel through the composition batch into
`RenderTargetSceneInfo`. The backend stages that scene's mask immediately before
the matching EGL, DMA-BUF, or shared-memory buffer commit. Mask changes also
request a redraw, including clearing the mask after dismissal.

Visibility slides keep shell material opaque: native blur cannot fade its
strength with Avalonia opacity. The volume HUD slides outside its native
viewport rather than fading into a visible blur silhouette. Blur scanlines are
not rebuilt for opacity changes that remain above the visibility threshold.

Regression coverage includes delayed first frames, spring overshoot and
refresh-rate independence, interrupted reversals, reduced motion, volume HUD
reopening during dismissal, and blur visibility thresholds.

Validation: all 29 combined motion, HUD, clock-blur, and screenshot tests passed,
including nested parent/material opacity thresholds. Theme-token and
whitespace checks passed. The rebuilt backend was loaded through a controlled
shell restart. Native captures exercised the volume HUD and launcher, including
their dismissed state; the HUD had full material opacity and no stranded mask
after dismissal. The original output volume was restored after the check.
These are sampled screenshots, not a high-frame-rate presentation recording.

Follow-up: slide entrances now use damping ratio 0.7 (about 4.6% overshoot).
Exits use a bounded accelerating curve without spring overshoot. Outward
velocity is retained when dismissing during an entrance; inward velocity is
discarded so dismissal cannot initially travel back onto the screen. Reopening
still starts from the currently rendered position and velocity. Toasts use the
same opaque visibility animation and travel their complete width plus the edge
margin, rather than fading while sliding only 82px.

Follow-up validation: all 27 motion, volume, and notification presentation tests
passed together. The click test now renders the card and moves the pointer into
the current window before pressing, avoiding stale pointer state from preceding
shell tests. Theme-token and whitespace checks passed. The running shell was
reloaded; a real D-Bus notification was displayed, captured, and closed. Its
appearance is saved as `artifacts/spring-and-blur-2026-10-18/toast-live.png`.

Launcher clipping follow-up: `LauncherView` inherited `ClipToBounds=true` from
Avalonia's `TemplatedControl`. Its fixed 620px host therefore cut off the card
during upward spring overshoot. The launcher host now permits overflow. The
card's rounded content clip and the native bottom-edge viewport clip remain.
The regression test checks the blur mask at a 30px upward overshoot pose and
renders that pose for inspection.
All 16 motion tests passed; the rendered overshoot pose shows an intact rounded
top and search field. Theme-token and whitespace checks passed. The fixed shell
was reloaded. The pose is saved as
`artifacts/spring-and-blur-2026-10-18/launcher-overshoot.png`.
