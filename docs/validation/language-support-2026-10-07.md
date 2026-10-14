# Language support validation — 2026-10-07

Implementation and primary-source research: [Languages and typography](../architecture/languages-and-typography.md).

## Automated evidence

- Main tests: **382 passed, 1 skipped, 0 failed**. Includes generated-resource loading, format arguments, Finnish decimal formatting, backward-compatible preference persistence, supported-language validation, missing POSIX locale fallback and explicit activation environment updates.
- GUI suite: **126 passed, 1 failed of 127**. The failure is the previously observed `LauncherIconLoaderTests.CatalogIconsLoadWithoutRealizedTilesAndRefreshWhenChanged` refresh timeout. Its isolated rerun passes. An earlier full run in this task passed all 125 cases before the two added Finnish layout cases.
- Focused control/Settings/language tests: **17 passed**. These render narrow/wide Settings and Finnish confirmations with 125%/150% type. The final seven dedicated language/font tests pass, including matching language-picker font size and height. They also render Inter, Noto Sans, DejaVu Serif and DejaVu Sans Mono at 90–150%, including accented Latin, Japanese, Arabic and Hindi text. The shaped runs contain no missing-glyph (index 0) entries in the tested sample. Default Inter controls remain 32px; larger fonts use one measured height across buttons, inputs, search and dropdowns.
- Catalog verifier: **962 neutral/Finnish keys**, complete coverage, matching format arguments and all XAML references resolved. Updating the catalog again adds zero entries.
- Session supervisor, native-session installer, ISO profile and Arch packaging checks pass. Session sources pass `bash -n`.
- All four application outputs contain the Finnish shell satellite assembly.
- Task baseline whitespace comparison: **111 changed existing files, no whitespace errors** at the recorded check. Git status/diff cannot run because the existing HEAD object is empty; the comparison uses saved task-start file copies. Unrelated changes were retained.

## Running desktop evidence

The native shell was rebuilt through `scripts/daisyos-native-development restart`; the same KWin session and user applications remained alive. The named desktop-settings request path accepted `language fi-FI` and Noto Sans/150%. A newly built native Settings instance displayed Finnish labels and the language picker. The user activation environment reported `LANG=fi_FI.UTF-8`, `LANGUAGE=fi:en`, and `LC_MESSAGES=fi_FI.UTF-8`.

The live check found and fixed the dropdown's remaining font/height override and the keyboard backend's status sentence. The corrected shared policy and Settings styles were included in the final application rebuild. Language/font preferences were restored to **English, Inter, 100%**, and the original KDE regional-format file and managed language-file presence were restored. First-launch locale handling preserves a valid inherited English UTF-8 locale. Only the validation-owned Settings instance was closed; the pre-existing Settings instance was retained.

Headless captures are under `/tmp/daisy-language-rendering`; private live desktop captures are in ignored `artifacts/language-support`. Existing third-party apps and older application processes need to reopen to load new binaries/preferences. Physical monitor scaling beyond the current native display is not covered by this evidence.

## Remaining environment limits

The complete `scripts/test.sh` gate validates catalogs, restores successfully, then encounters the existing bundled Avalonia DesignerSupport command `dnx Bun.Unofficial.Tool --yes -- install`, which exits 127 because `dnx` is absent. The solution build was stopped after recording that failure; it is not an all-green solution gate. Focused application builds and the suites above are the usable evidence.

The session import source changes are ready and installer-tested, but `/usr/local/bin/daisyos-native-development-session` is root-owned and `sudo -n` requires a password. Install it with:

```bash
sudo scripts/install-native-development-session.sh
```

This step updates next-login compositor/session locale imports. Shell-driven app launches and D-Bus activation already receive the user language in the current session. The ISO sources include the change.
