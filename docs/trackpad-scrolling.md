# Trackpad scrolling

DaisyOS uses one compositor speed setting, with continuous deltas converted once
to content distance. Trackpad input must never use the 50-pixel mouse-wheel step.

The native Firefox/GTK Wayland default is the compatibility reference:

- GTK converts Wayland axis units to smooth deltas by dividing by 10 in
  [gdkdevice-wayland.c](https://github.com/GNOME/gtk/blob/gtk-3-24/gdk/wayland/gdkdevice-wayland.c).
- Firefox's pixel-mode pan path multiplies those deltas by 40, with output scaling
  handled separately. See [nsWindow.cpp](https://github.com/mozilla-firefox/firefox/blob/main/widget/gtk/nsWindow.cpp)
  and [StaticPrefList.yaml](https://github.com/mozilla-firefox/firefox/blob/main/modules/libpref/init/StaticPrefList.yaml).

Consequently a compositor-scaled Wayland delta of 1 corresponds to 4 logical
content pixels in DaisyOS scroll views, closed dropdown forwarding, and launcher
paging. `PointerWheelEventArgs.GetScrollDelta` owns this conversion. Mouse-wheel
steps retain their existing control-specific sizes. Logical scroll panels convert
content pixels to item units using their viewport dimensions. Fractional movement
and immediate reversals are retained; snap points apply after finger release.

The speed slider retains factors 0.1, 0.2, 0.3, 0.4, 0.5, with the middle factor
0.3 as the default. KWin receives this factor once and supplies scaled input to
both native applications and external clients. It is not reapplied in the shell.

KWin reads hotplug defaults from `[Libinput][Defaults][Touchpad]` in `kcminputrc`.
The ISO seeds this group, and Settings updates it alongside live devices. Startup
migrates a legacy `[Touchpad]` preference into that default group and leaves
explicit per-device settings intact. KWin's default-group lookup is in
[Connection::applyDeviceConfig](https://github.com/KDE/kwin/blob/master/src/backends/libinput/connection.cpp).

## Desktop magnification

DaisyOS disables KWin's Zoom effect (`[Plugins] zoomEnabled=false`) in session setup and bridge installation. This removes its built-in three-finger pinch magnification, including the effect's keyboard magnification actions. Three-finger overview swipes and four-finger launcher pinches remain available. The bridge installer also unloads an already-running Zoom effect.

## Gesture release behavior

Launcher paging and the three-finger overview swipe track motion directly while
fingers are down. Their velocity trackers accumulate small reverse samples so a
high-rate trackpad can cancel a flick without retaining momentum in the original
direction. Sub-threshold sensor noise does not reset the velocity history.
A short overview flick commits at 3% travel with a release speed of at least
one normalized gesture distance per second. It does not need to project past
the halfway point; slow drags still commit at 50% travel. The same rule applies
to opening and closing, while a deliberate reversal cancels the flick.
Release velocity starts decaying after a 32 ms delivery allowance and expires
after 100 ms without motion, so a paused gesture does not retain full flick speed.

When checking hardware, include a fast outward swipe followed by a slow reversal
made of tiny movements, and releases after short pauses. Check these in both
directions, including above the normal distance threshold.

KWin can emit swipe cancellation during axis selection or a direction change,
as well as on finger release below its distance threshold. Overview release
handling waits one event turn: continued movement keeps tracking, and a direction
handoff cancels instead of falsely committing a flick. Velocity is captured at
the cancellation event so the deferred decision does not lose momentum.

The v16 effect ID gives live upgrades a new QML URL. Unloading/reloading the same
URL can retain old components in KWin's shared engine. Installation migrates v15
theme and motion preferences and disables/unloads older versions.

The overview stops its settle animation before handing control back to fingers.
Interrupted swipes retain full sensitivity, and a release during asynchronous
preview preparation settles from the last reported finger position. Opening and
closing both decelerate into their endpoint.

## Verification

Run the overview gesture state and motion checks with:

```sh
scripts/test-task-overview-gestures.sh
```

The state tests run the production QML transition logic with a fake KWin scene;
they exercise animation interruption and delayed preview readiness without
requiring a compositor. Thumbnail rendering and physical input latency still
need a live session.


```sh
dotnet test tests/DaisyOS.ShellDemo.Tests/DaisyOS.ShellDemo.Tests.csproj \
  --filter 'FullyQualifiedName~TrackpadScrollingTests|FullyQualifiedName~ComboBoxBehaviorTests|FullyQualifiedName~LauncherPagingTests'
dotnet test tests/DaisyOS.Tests/DaisyOS.Tests.csproj \
  --filter FullyQualifiedName~InputHardwareService
```

On hardware, compare Settings, Files, Notes, and Firefox running natively on
Wayland at 100% page zoom. Check short movements, diagonal scrolling, reversals,
edges, dropdowns, and external displays at different scale factors. Test both a
newly connected trackpad and a device with saved preferences.

These changes align the default distance mapping. Browser preferences, page
scripts, application-specific scrolling, and fling physics can still change the
result. Automated tests verify distance conversion and persistence, not a claim
that every third-party app has identical motion or that every trackpad has been
physically calibrated.
