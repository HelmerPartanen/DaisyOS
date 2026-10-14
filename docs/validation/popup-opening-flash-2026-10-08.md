# Popup opening flash — 2026-10-08

The reported white rectangle matches the native popup host's default white background fading into the Fluent popup theme's null background. The shared theme transition policy was adding a brush transition to `PopupRoot.Background`. The earlier tooltip tests rendered the tooltip card after settling; the headless popup factory uses overlays, so those tests did not cover the native host's opening transition.

Exclude only `PopupRoot.Background` from automatically added theme transitions. Surface transparency now changes immediately while menu/card and other foreground/color transitions remain enabled. Retain the Wayland tooltip blur exemption, which addresses the separate black-corner issue.

Eleven focused GUI tests pass: a directly constructed native `PopupRoot` checks immediate white-to-null background behavior and rendered pixels, card background transitions remain installed, tooltip corner alpha is checked in both themes at normal/fractional scale, and theme synchronization regressions pass. Existing bitmap API/analyzer warnings remain in the test project. No Wayland backend change was needed for this fix.

Physical opening-frame acceptance remains with the user; headless rendering does not establish compositor presentation timing.

Files, Notes and Settings rebuilt with zero warnings/errors. Native shell reload completed (29548 → 32320), preserving KWin (1042). Strict compositor preflight returned zero failures/warnings and the new shell started without transition exceptions. Root and Avalonia whitespace checks pass. Existing system-app processes must be reopened once to load the updated policy.
