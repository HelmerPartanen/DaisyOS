# Window snapping

DaisyOS delegates drag geometry and tiling to KWin. Its work areas account for
panels, output coordinates/scaling and application size constraints. No DaisyOS
script writes client geometry during a drag.

`scripts/configure-kwin-snapping.sh` applies the policy immediately. Session
startup also runs it before KWin starts:

- Free dragging does not attract windows to other windows or the screen center.
- A small 8-logical-pixel screen-edge zone only acts after crossing the work-area
  boundary; it does not pull a nearby window away from the pointer.
- Dragging the pointer to a side tiles to a half; the top maximizes.
- Quarter tiling uses the top/bottom 10% of a side edge, reducing accidental
  quarter tiling when the intended target is a half.
- Native keyboard shortcuts and explicit custom tiling remain available. Saved
  custom tile layouts are preserved.

DaisyOS window motion cancels positional animations as soon as an interactive
move or resize starts. Restoring a maximized window during a drag also bypasses
the maximize/restore animation, keeping the window under the pointer.

Validation uses the Qt JavaScript engine for drag/animation ordering and a
separate KWin instance with a test-only native effect for actual compositor
snap calculations and half/quarter geometry. The native probe is never installed
into the user's plugin directory.

```sh
python3 packaging/kwin/daisyos-window-motion/tests/test_motion.py
cmake -S tests/kwin-snapping -B /tmp/daisy-snap-probe
cmake --build /tmp/daisy-snap-probe
scripts/test-kwin-snapping.sh /tmp/daisy-snap-probe/daisysnaptest.so
```

The probe needs the same KWin/Qt development dependencies as the native frame
effect. On hosts using the frame installer's cached ECM/Vulkan headers, pass
its ECM prefix as `CMAKE_PREFIX_PATH` and its headers as `Vulkan_INCLUDE_DIR`.
The runtime test needs PySide6 and qdbus6. It verifies free movement, neighbor
non-attraction, a disabled center magnet, bounded work-area edge snapping and
native half/quarter positions. It does not simulate physical pointer movement
across every multi-monitor arrangement.
