# Window titlebar corner clipping — 2026-11-14

The session preferred a user decoration module installed on October 22 over
both the system module and current source. That old module lacked
`DaisyDecoration::titleBarClip`, so its opaque titlebar painted square pixels
outside the compositor's rounded window outline.

Built the current native decoration in Release and replaced the user module
atomically. KWin retained the original module in memory even after switching
to Breeze and back. Loaded an identical temporary build with plugin ID
`org.daisyos.decoration.rounded` to activate the correction without restarting
KWin or closing applications. The next native development login selects the
normal `org.daisyos.decoration` module, which is now updated as well.

Validation:

- Native titlebar pixel regression passed: both corners, caption overflow,
  close-button hover, narrow windows, square/maximized state and
  100/125/150/200% scaling.
- Captured the same temporary native dialog before and after activation.
  At image coordinate (23,22), the old titlebar was opaque #171717; the
  corrected corner is transparent. The interior remains opaque #171717.
- KWin reports `org.daisyos.decoration.rounded` as active. KWin PID 3254 was
  preserved. The temporary verification dialog closes itself.
- Captures and original plugin backup are in
  `artifacts/window-corners-2026-11-14/`.
