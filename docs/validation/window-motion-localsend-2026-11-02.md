# Window transition continuity and multiple-file sharing

## Window shift

Source inspection found two causes of apparent movement in the motion effect: close/hide shrank a window to 96%, moving its edges, and a new minimize/close could cancel an active animation before starting at an untransformed endpoint. That cancellation discards the currently painted transform.

Close/hide now fades in place. Opening, closing, minimize/restore and maximize use the same three channels (scale, translation, opacity), allowing interruptions to use KWin's `retarget` operation. It starts from the existing interpolated value rather than resetting client geometry. See [KWin's implementation](https://github.com/KDE/kwin/blob/v6.8.1/src/effect/animationeffect.cpp#L290). Geometry interpolation remains based on the old/new frame center and dimensions. Interactive dragging still cancels transforms immediately, and overview/lock/fullscreen exclusions and Reduce Motion remain enforced.

The Qt JS motion regression checks opening-to-minimize, rapid restore, close during opening, stationary close endpoints, minimize's initial position/size, geometry changes, drag interruptions, exclusions and reduced motion. The continuity assertion fails against the previous source and passes against the modified effect. This is evidence for the source-level reset problem; the physical shift shown in the supplied photos has not been reproduced or pixel-certified in the native compositor.

## Multiple-file LocalSend

The transfer service and file picker already accept multiple files. The Files context-menu handler passed only the context entry's path, discarding the rest of a selection. It now passes all selected files when the context entry is selected, and only that entry when it is outside the selection. Directories are excluded; folder archiving is unchanged.

Reopening the chooser seeds its selection from the queued files. Sending uses an immutable path snapshot. The chooser and Send action remain disabled during transfer, and reopening the sheet cannot reset busy state or replace the running batch. Retry restores the actions after failure.

## Verification

- Debug shell build succeeded, with the existing unused Settings Bluetooth-field warning.
- Two new LocalSend headless checks passed: multi-selection/context routing, chooser selection retention, and a single two-file transfer with duplicate-submission protection.
- Files polish (5), general usability (19), and portal picker (3) checks passed in isolated processes: 29 checks in total with the new tests.
- `python3 packaging/kwin/daisyos-window-motion/tests/test_motion.py` passed in Qt's JS engine.
- `git diff --check` passed.
- No real LAN transfer or live KWin effect reload was performed. The running desktop was not restarted.
