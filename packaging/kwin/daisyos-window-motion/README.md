# DaisyOS Window Motion

KWin 6 JavaScript effect, installed by `scripts/install-kwin-window-bridge.sh`
(or independently by `scripts/install-kwin-window-motion.sh`).

- Open/unhide: 180 ms, 96% to full size with a fade.
- Close/hide: 140 ms, full size to 96% with a fade.
- Minimize/restore: reversible 200 ms scale, translation and fade toward the
  taskbar icon when KWin supplies icon geometry; subtle downward motion otherwise.
- Maximize/restore: 220 ms size and position interpolation.

All timings use KWin's animation speed setting. DaisyOS Reduce Motion disables
new transitions and cancels running transitions through effect reconfiguration.
The effect excludes shell surfaces, popup windows, screen locks and fullscreen
windows, and yields to fullscreen effects such as the task overview. Animations
use KWin's existing textures without timers, shaders, screenshot captures or
client geometry writes. Existing blur and frame roles are never overwritten.

The installer disables competing scale/fade/minimize/maximize effects and loads
the package in the running session. An inherited zero KWin animation speed is
reset to 1 during installation; custom positive speeds are preserved. DaisyOS
Reduce Motion remains the explicit opt-out. Popups retain their existing motion.

Verification:

```sh
python3 packaging/kwin/daisyos-window-motion/tests/test_motion.py
bash -n scripts/install-kwin-window-motion.sh
# In a live KWin Wayland session, with PySide6 and qdbus6:
python3 scripts/test-kwin-window-motion-runtime.py
```

The Qt JS tests cover rapid reversals, signal ordering, resize interruption,
close surface lifetime, shell/lockscreen exclusions, overview and reduced motion.
The live test creates a disposable Wayland client and uses KWin commands for
minimize/restore/maximize. It asserts that this effect is active during every
transition and inactive afterward, rather than merely checking for script errors.
This does not establish frame pacing across every GPU or multi-monitor layout;
visual acceptance on target hardware remains necessary.

API references: [KWin EffectWindow](https://invent.kde.org/plasma/kwin/-/blob/master/src/effect/effectwindow.h)
and [ScriptedEffect](https://invent.kde.org/plasma/kwin/-/blob/master/src/scripting/scriptedeffect.h).
