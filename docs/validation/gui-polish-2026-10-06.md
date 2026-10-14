# GUI polish validation — 2026-10-06

The widget library now fits its previews into a grid without scrollbars. Photos
use rounded image crops and short size labels. Dragging from a scaled preview
preserves the pointer's relative grab position on the desktop widget.

Settings uses plain language for sound, network, display, input and software
information. The shared file picker handles unrestricted files or caller-specific
extensions, uses a full-height sidebar and packs filtered results without empty
tile slots. Notes, file sharing and video wallpaper selection use the shared
picker. Existing photo-selection cancellation and replacement semantics remain.

Files and Settings navigation use the same 4px item gap as the picker. Calendar
today markers are opaque white with black text in dark mode and black with white
text in light mode, including the desktop calendar widget.

Files reports actual extraction results instead of assuming an external command
succeeded. Destination creation is protected by the extraction error handler.
File errors present recovery steps while retaining diagnostic details in logs.

## Automated evidence

- Main suite: 373 passed; one opt-in KWin capture test skipped.
- Full GUI suite: 92 passed, including Settings pages in both themes and narrow/wide layouts.
- Shell suite: 125 passed.
- Final focused GUI checks: 11 passed after the last resource and copy edits.
- All 12 script checks listed after the .NET tests in `scripts/test.sh` passed.
- The native restart fixture now covers failed app installation without terminating its fixture shell.
- `git diff --check` passed.
- Rendered picker and widget-library frames were inspected in light and dark themes.

The complete `scripts/test.sh` gate was attempted twice. The solution build
fails in upstream Avalonia DesignerSupport: initially `dnx` is absent (127);
a temporary wrapper to the installed SDK's `dotnet dnx` then reaches the Bun
build tool but it exits with code 132. No Avalonia files were changed to bypass
this failure. App-specific builds and all DaisyOS suites pass separately.

## Native evidence and limits

The strict live Wayland preflight reports no required failures and no warnings.
The native development helper rebuilt the standalone applications and reloaded
the shell while preserving KWin and open applications. Existing Notes instances
must be reopened to load their new picker code. Calendar color and picker layout
acceptance here are based on GUI render and interaction tests, rather than a
claim of complete live pointer, focus or compositor acceptance.
