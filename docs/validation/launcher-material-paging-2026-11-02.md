# Launcher material and page flicker

The launcher now uses the taskbar's three-layer frame: an antialiased outer edge, transparent inner frame with `ShellSurfaceInnerBorderBrush`, and a separate `SurfaceBaseBrush` fill. The launcher uses the shared XXL corner-radius tokens (32px outer, 31px inner frame, 30px fill), while keeping the taskbar material and border strokes. The opaque gaming fallback changes only the fill, preserving the frame.

Launcher pages are already bounded by pagination. Their tile panels now retain every tile while translated through the clipping viewport; Files keeps its existing viewport virtualization. Incoming and outgoing page grids finish layout before a page animation or swipe handoff begins. A newly encountered swipe neighbour is prepared once, rather than relaid out on every gesture update.

Regression coverage includes both themes' material/border equality with the taskbar and XXL corner radii, actual cached bitmap sources before the first transition frame, rapid page reversals, swipe preparation and commit, and keeping tiles realized when a page is clipped. Existing paging, panel-size, motion/blur, icon-loading and Files checks are rerun separately.

Artifacts and test logs: `artifacts/launcher-material-paging/`. This is component/headless verification. The running desktop was not restarted, and physical KWin frame pacing has not been certified.

Validation: Debug build succeeded; all 61 focused checks passed across six isolated groups. `git diff --check` passed.

## Follow-up: icons briefly showing another application's image

The previous bitmap regression used the same icon for every app, so it could not detect an identity mix-up. The distinct-color headless check did not reproduce the reported native flash, but inspection found a handoff that could expose recycled visuals: after a swipe settled, the already-visible incoming grid was discarded and the old current grid was rebound to the destination page.

Committed swipes now promote the existing incoming grid to current. Its cached page snapshot, tile controls, data contexts and image sources remain intact. The two physical grids exchange roles; page contents are assigned explicitly so a fixed XAML binding cannot reset the wrong buffer. Cancelled swipes keep their current buffer.

`LauncherIconIdentityTests` assigns a different solid-color bitmap to every application and checks both source identity and rendered center pixels throughout forward/backward swipes, with animation enabled and disabled. It also asserts that the incoming grid and its tile instances survive settlement. That last invariant fails against the old implementation and passes with the promotion change. Logs are under `artifacts/launcher-icon-identity/`; this does not certify the physical KWin rendering path. The running session was not restarted.

Follow-up validation: Debug build succeeded and all 77 focused checks passed across seven isolated groups. `git diff --check` passed.
