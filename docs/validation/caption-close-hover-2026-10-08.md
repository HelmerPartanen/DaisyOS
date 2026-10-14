# Caption Close hover — 2026-10-08

App-owned title bars applied both neutral caption-hover styling and Close's danger styling. Automatic presenter background transitions could retain an intermediate neutral fill before the red settled. Neutral hover/pressed selectors now exclude Close in the shared title bar, Files, Calendar, and Calculator. Close's presenter opts out of automatic color transitions and has no interaction brush transition, so hover/pressed danger colors apply immediately. Other button and theme motion remains enabled.

Nine focused GUI tests passed: repeated pointer entry asserts the exact hover danger color immediately and after 20 ms, for Settings/shared chrome and Files in light/dark themes; existing sidebar/caption and theme synchronization tests also pass. This is automated rendered-control evidence; physical display acceptance remains separate.

Existing profile-picture edits and Avalonia submodule changes were preserved. Reopen an existing app window after rebuilding to load its new styles.

Files, Notes and Settings rebuilt with zero warnings/errors. The shell was reloaded with KWin and open applications retained; strict live compositor preflight passed all nine requirements. Whitespace checks passed.
