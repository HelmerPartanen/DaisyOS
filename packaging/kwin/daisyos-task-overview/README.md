# Launcher and overview gestures

KWin recognizes these gestures globally, including over application windows:

| Gesture | Action |
| --- | --- |
| Four-finger pinch inward | Reveal the launcher |
| Four-finger pinch outward | Dismiss the launcher |
| Three-finger swipe upward | Reveal task overview |
| Three-finger swipe downward | Dismiss task overview |

Panels follow gesture progress, including reversals. Release past halfway or
make a short, deliberate flick to complete the overview gesture. Slow movements
below halfway return to the original state; pausing clears flick intent and
reversing overrides outward momentum. Reduced motion keeps direct progress
feedback and skips the settling animation. Pinches retain their distance-based
release decision.

The launcher also supports two-finger paging with direction locking, velocity
projection, and resistance at the first and last pages. A new gesture can interrupt
settling without resetting the displayed position.
Paging measures recent movement using input timestamps, including batched and
single-sample flicks. Each flick commits at most one page and never passes the
first or last page. Faster releases settle sooner; holding a finger stationary
before release does not count as a flick.

Motion samples are coalesced before the shell's render cycle, while release
events remain ordered. Icon decoding uses a bounded worker pool and publishes
results below input/render priority. Opening does not force layout or wait for
catalog icons, and unchanged pages keep their existing tiles. Overview previews
scale fixed-size render content instead of resizing their masked textures for
every finger movement.

Both the shell and this KWin effect must be updated. The existing
`scripts/install-kwin-window-bridge.sh` installer installs the gesture component
alongside the overview. Restart the shell after rebuilding it.

## Validation

From the repository root:

```sh
dotnet test tests/DaisyOS.ShellDemo.Tests/DaisyOS.ShellDemo.Tests.csproj --filter FullyQualifiedName~LauncherPagingTests
dotnet test tests/DaisyOS.Shell.Tests/DaisyOS.Shell.Tests.csproj --filter 'FullyQualifiedName~LauncherSwipePhysicsTests|FullyQualifiedName~LauncherPinchDispatcherTests'
QT_QPA_PLATFORM=offscreen /usr/lib/qt6/bin/qmltestrunner \
  -import packaging/kwin/daisyos-task-overview/tests/imports \
  -input packaging/kwin/daisyos-task-overview/tests/tst_GlobalGestures.qml
QT_QPA_PLATFORM=offscreen /usr/lib/qt6/bin/qmltestrunner \
  -input packaging/kwin/daisyos-task-overview/tests/tst_TransitionGeometry.qml
QT_QPA_PLATFORM=offscreen /usr/lib/qt6/bin/qmltestrunner \
  -input packaging/kwin/daisyos-task-overview/tests/tst_OverviewTransitionFrame.qml
QT_QPA_PLATFORM=offscreen /usr/lib/qt6/bin/qmltestrunner \
  -input packaging/kwin/daisyos-task-overview/tests/tst_FlickMotion.qml
```

The gesture QML tests use test doubles for KWin recognition and D-Bus transport;
they exercise the production gesture routing and queue. They cannot establish
physical recognition reliability, latency, or smoothness. On a trackpad, verify
partial releases, direction reversals, stationary holds, quick repeated gestures,
and gestures over fullscreen apps, with reduced motion both enabled and disabled.
