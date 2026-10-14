# DaisyOS shadow-only decoration policy

DaisyOS uses KWin's native shadow-only decoration path for client-decorated
top-level windows. This is intentionally not a scripted effect and does not
force server-side titlebars.

## Compositor baseline

The distro KWin and KDecoration build must contain the upstream shadow-only
decoration work before enabling the policy:

- KDecoration commit `64cf546` (`Add support for shadow-only decorations`)
- KWin commit `b70fc02` (`Add support for client side decorations with server side drop shadows`)
- KWin follow-up `8c575b0` (`Don't try to apply server side drop shadows to all X11 windows`)

The Daisy decoration probes for `KDecoration3::Style::Shadow` at configure
time. On an older ABI it remains a titled-only plugin even though its metadata
is forward-compatible. Do not enable a shadow policy against that older ABI.

## Daisy policy contract

Apply `DecorationPolicy::Shadow` only after the ordinary KWin window-rule
policy has been evaluated, and only when all of these are true:

- the selected Daisy decoration advertises `KDecoration3::Style::Shadow`;
- the window is an `XdgToplevelWindow` or managed `X11Window`;
- it is a normal window or dialog;
- the client-selected mode is client-side/frameless;
- it is not fullscreen, internal, unmanaged, lock-screen, desktop, dock,
  menu, popup, tooltip, notification, applet popup, OSD, critical
  notification, or any layer-shell surface;
- no user/window rule explicitly selected `None`, `Server`, or `Client`;
- its resource class is not listed in the `[Policy] ExcludedApplications`
  entry of `daisyos-frame-policyrc`.

This belongs in one compositor helper, `DaisyFramePolicy::shouldApply(const
Window *)`, called by the XDG and X11 `preferredDecorationMode()` paths. The
helper must return a policy decision only; rendering stays in KDecoration.
Wayland clients with ambiguous subsurface shadows belong in the exclusion list
until KWin has reliable margin semantics for them. Never classify shadow
pixels by color.

## State behavior

The plugin gives shadow style zero layout borders and an empty titlebar. KWin
continues to own input/resize geometry. `BorderRadius` clips the client surface;
the double edge is embedded into the cached `DecorationShadow` nine-patch so
it does not displace content. Maximized and fullscreen states suppress the
external shadow/frame; titled and shadow styles use the same frame metrics,
colors, radius and shadow factory.

No rounded-corner effect is enabled by default. Add an effect only for a
specific clipping failure that KWin's native `BorderRadius` cannot handle.
