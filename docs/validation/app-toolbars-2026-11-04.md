# Application toolbar validation — 2026-11-04

## Changes

- Centralized application toolbar material, padding, minimum height, circular icon template, and interaction feedback in `Styles/AppToolbarStyles.axaml`, included by `ComponentStyles.axaml`.
- Toolbar corners follow half the arranged height. Icon-button width follows height, and corners follow half that height. The shared template avoids competing Fluent presenter fills and whole-button press transforms.
- Migrated icon-only app actions, including Calculator, Calendar, Files, the picker, Notes, App Store, Mail, and the UI kit. Window caption controls retain their existing contract.
- Files has independent navigation, breadcrumb, and search/view pills. The picker and Notes image controls also share toolbar styling. Mail action groups and the Disks action toolbar use the shared host.
- Notes' primary toolbar contains text style, bold/italic/underline, image insertion, and More. Advanced formatting remains accessible through labeled overflow controls. Selection survives opening the menu; repeated font-size changes leave it open. Other format choices restore editor focus. Document changes close the previous menu.
- Added usage guidance in `docs/app-toolbars.md` and linked it from CONTRIBUTING.md. Added neutral/Finnish resources for the new labels.

## Checks

Release build completed without errors. The final focused UI run passed **50 tests**, covering shared geometry at 24/32/40/48 px, press and checked state, resizing, shared resource updates, Notes formatting/undo/input/image handling, repeated size changes, selection preservation, document switching, Files layouts, picker behavior, Calendar, Disks, and the preceding widget interaction regressions.

```sh
DAISYOS_UX_ARTIFACTS=/tmp/daisy-toolbars-visual dotnet test tests/DaisyOS.ShellDemo.Tests/DaisyOS.ShellDemo.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~AppToolbarTests|FullyQualifiedName~NotesEditorTests|FullyQualifiedName~FilesPolishTests|FullyQualifiedName~PortalPickerTests|FullyQualifiedName~CalendarPanelTests|FullyQualifiedName~DiskManagerTests|FullyQualifiedName~WidgetInteractionRegressionTests' -m:2
```

Reviewed rendered Notes at 720 and 1280 px, its advanced formatting menu, and Files at 1040 px. Automated layout coverage includes Notes at 720/960/1280/2560 px in light/dark themes and Files at narrow widths with 100%/150% typography. Whitespace checks pass.

Full localization verification still stops at the existing unlocalized `Widget Gallery` literal in `WidgetLibraryOverlay.axaml`; the four new sources have matching neutral and Finnish entries. Existing unrelated build warnings remain.

These are headless rendered-control and functional checks. The running desktop was not restarted, and native KWin presentation/frame pacing was not measured for this change. Passing these checks does not establish the absence of all GPU- or compositor-specific glitches.
