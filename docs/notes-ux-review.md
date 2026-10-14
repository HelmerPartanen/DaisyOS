# Notes editor usability review

The rich/code editor work introduced several failures in the actual editing path. The fixes retain the document/package formats and use DaisyOS's shared surfaces, typography, borders, radii, and focus styles.

## Findings and changes

| Problem | Resulting behavior |
|---|---|
| Code editor lacked the AvaloniaEdit control theme; its buffer could exist without a visible writing surface. | The editor loads its required theme locally. Text, syntax highlighting, and the control surface follow the active window theme. |
| A hidden Markdown pane consumed half the available editor width. Its visibility bindings pointed at the notes model instead of the editor. | Normal editing uses the whole pane; control state binds to the editor itself. Code defaults to no wrapping, plain text wraps, and file indentation settings are respected. |
| The formatting toolbar was attached to the pane, with a second incorrectly positioned selection toolbar. | The floating toolbar uses the sidebar background and the same 40px height and radius as Files. The bar sizes to its content, stays centered in the document panel, and caps its width on narrow windows, which expose horizontal scroll arrows. Buttons remain keyboard accessible and formatting returns focus to the writing surface. |
| Fixed document width and horizontal list panels prevented narrow layouts from wrapping correctly. | The document fits its pane, starts 24px from the document panel left edge and fills the available width, and uses normal line spacing without extra paragraph gaps. Lists give their text a constrained column, and image width is capped to the document. |
| Synchronizing toolbar choices fired edit handlers and could rebuild the document during focus/selection changes. Heading choices did not match their levels. | Toolbar synchronization is guarded, does not mutate content, and reflects heading level, weight, family, and size. |
| Holding a printable key inserted only the initial character. | Wayland repeat ticks now dispatch text through the same guarded path as initial presses. Handled shortcuts, releases, and control sequences do not insert text. Ten backend regression cases pass; physical held-key behavior still requires a running-session check. |
| Formatting without a selection silently did nothing. | Formatting sets the style for subsequent typing, can be turned on and off, and retains an explicitly disabled style when returning to the same caret. Blank Select All does not swallow formatting. |
| Selection direction, arrow movement, wrapped-line navigation, and Unicode deletion were unreliable. | Shift selections can reverse, arrows move through wrapped lines and neighboring blocks, word movement/deletion works, and deletion respects grapheme boundaries. Shift+Enter inserts a line break. |
| Enter flattened styled runs; Backspace following an image could delete the text block. | Splits preserve run styles, selected text is replaced, and merging requires a text destination. Delete can join adjacent text blocks. |
| Rich editing had no clipboard actions or document undo. | Cut/copy/paste, an editing context menu, whole-note selection, cross-paragraph drag selection, and bounded document undo/redo cover text and structural edits. Input methods receive surrounding text, selection, and caret geometry. |
| The window cleared focus when clicking the custom rich editor. | Pointer focus stays with the editor, including after toolbar formatting. Empty text blocks paint a transparent hit region, and clicking the blank writing area restores focus. New notes focus their writing surface. |
| Rich edits left cached sidebar titles and modification dates stale. | Document edits refresh note metadata. Unsaved state has a dot instead of italicizing the title. |
| Duplicate converted rich notes to plain text, losing formatting and image blocks. Code duplicates became .txt files and were not reloaded. | Rich duplication preserves independent document structure and assets. Text/code duplication retains the extension; the notes folder loads saved text/code files as well as rich notes. |
| Pins were UI-only, and Rename only selected the note. | Rich pins persist in the package. Edit title opens a shared-style dialog; leaving the title blank restores the first-line title. |
| Conversion deleted the original .txt even after a failed save. | Create rich copy saves a separate rich note and retains the original. A failure preserves the original selection and reports the error. |
| Binary files could be decoded into editable text, and damaged rich packages could escape the open handler. | These files produce a visible error without replacing the selected note. Unreadable saved notes are reported instead of silently disappearing. |
| Image editing used an invalid cursor name and retained decoded bitmap resources after rebuilds. | Image controls open safely, remain within the document, expose controls on keyboard focus, and dispose detached bitmaps. Detached text blocks stop their caret timers. |

The sidebar divider has been removed. Shared dropdown fields use a quiet inset surface, centered vertical padding, focus-only borders, and short brush transitions. Adaptive dropdown sizing respects explicit widths instead of widening compact toolbars. The test application now loads the production dropdown styles. Toolbar dropdowns explicitly override the global 40px minimum with a 32px minimum and use the smaller inner-control radius, giving all three fields 4px top/bottom clearance. The inherited Fluent highlight overlay is removed so keyboard focus uses the OS border without a second fill/border. Focused-state checks measure the actual template border and render screenshots for all six theme/size combinations. The follow-up passed 26 editor checks, including assertions for each dropdown height and clearance in both themes at all three widths.

Opening or switching rich notes adds a subtle 100ms opacity transition without moving text or delaying focus. It is omitted when reduced motion is enabled. Layout checks also resize each rendered window down to 720px and back up to 1280px. Maximized-width cases at 2560px verify that the writing surface stays 24px from the left edge while the toolbar remains centered; the old maximum-width constraint that centered the writing surface has been removed.

## Validation

Final checks: 35 Notes interaction/layout tests passed; 45 document, package, format-detection, and encoding tests passed. The standalone `DaisyOS.Notes` build passed with zero warnings and errors. `git diff --check` passed. Shared dropdown style checks passed (3); isolated usability, adaptive-layout, and workflow-recovery classes passed (18, 2, and 14 respectively). Mixed-class runs encountered Avalonia headless dispatcher ownership errors during test cleanup; the affected classes pass in separate test processes.

`tests/DaisyOS.ShellDemo.Tests/NotesEditorTests.cs` covers real headless windows, pointer focus, toolbar actions followed by typing, clipboard operations, cross-paragraph replacement, undo/redo, styled splits, Unicode deletion, saved-file output, editor switching, duplicate/reload, and conversion/open failures. Existing Notes save, failed-close, and failed-deletion tests are included.

Rendered Notes screenshots cover light/dark themes at 720×480, 960×480, and 1280×480, with separate existing error-state checks at 720×480 and 960×640. Generated images are in `artifacts/notes-ux/`. These validate Avalonia layout/rendering, not live KWin behavior.

The caret uses the normal text brush and the same one-pixel, pixel-aligned geometry as Avalonia text controls. Image import uses the shared file picker directly, with a tested pick-confirm-import-save path and visible decode/read errors.

## Remaining validation boundaries

Native Wayland input-method composition, clipboard integration, and physical keyboard interaction still require a running-session check. The custom rich editor supplies input-method context but does not render inline preedit text. The Markdown pane still mirrors source text rather than rendering Markdown; it is not exposed as a finished preview action. Package fields such as image text wrapping and inline links are not implemented as editing workflows.
