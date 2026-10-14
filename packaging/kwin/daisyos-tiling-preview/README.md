# DaisyOS tiling preview

Replaces KWin's outline visual through `[Outline] QmlPath`, using the native
`outline` context supplied by KWin. KWin retains all snap geometry, placement,
window state, drag cancellation, stacking, and preview lifetime management.
There is no shell overlay, polling loop, custom tiling logic, blur pass, or shader.

The frame uses a 6 px visual inset, DaisyOS's application corner radius, a thin
accent outline and a light translucent fill. It reads the current exported
`daisyos-window-decorationrc` palette at each activation, including the accent
and Reduce Motion values. Global KDE animation speed also applies.

Geometry is interpolated in global coordinates over 160 ms with OutCubic easing.
Retargeting begins at the currently displayed rectangle, with one progress
animation for all four edges. The native surface covers the union of the current
and target rectangles, including negative monitor origins. Changes to KWin's
`unifiedGeometry` do not reset the animation. KWin destroys the outline surface
on hide, so dismissal is immediate and cannot leave an overlay behind.

Install with `scripts/install-kwin-tiling-preview.sh`. The window bridge installer
also installs this visual for normal DaisyOS sessions. Installation is per-user
and leaves the system Breeze files untouched. KWin 6.7 caches the outline
component until the compositor exits: if the old preview has already been shown,
the new visual appears after logging out and back in. Reconfiguring KWin or
restarting only the DaisyOS shell cannot replace that cached component.

To restore the system outline, delete the override and log in again:

```sh
kwriteconfig6 --file kwinrc --group Outline --key QmlPath --delete
```

Checks:

```sh
scripts/test-kwin-tiling-preview.sh
scripts/test-kwin-tiling-preview.sh --runtime
```

The runtime check uses a private D-Bus session, a virtual KWin output, isolated
configuration and software rendering. It loads the actual native outline path,
checks intermediate animated rectangles, settles at the requested target, and
checks hide/reopen behavior. Logs are under `artifacts/compositor/tiling-preview`.
Physical drag smoothness and the final on-desktop appearance still need a user
check after the next login.
