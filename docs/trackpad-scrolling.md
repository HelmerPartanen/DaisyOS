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

## Verification

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
