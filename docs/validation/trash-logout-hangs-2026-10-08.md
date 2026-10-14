# Trash and logout hangs — 2026-10-08

The user reports that Empty Trash freezes the desktop and Log out leaves a black screen. Inspection found recursive Trash deletion running on the UI thread, and session cleanup waiting indefinitely after sending SIGTERM to the shell or KWin. These are concrete hang paths; the user's destructive actions were not reproduced on their live session.

Empty Trash now closes its native context menu first and deletes files on a worker. Concurrent requests share the in-flight task. Failed payload deletion retains its restore metadata and reports a translated error; orphan metadata is removed. The existing home Trash scope is preserved.

Native logout launches a bundled guard in an independent user service in `daisyos-core.slice`. It validates user ownership and supervisor-child relationships, requests normal supervisor shutdown, then bounds waits and escalates only for the session's children. It works with the currently installed older supervisor. The source session launcher also bounds its child shutdown waits; the root-owned installed launcher was not replaced. Power menu handlers catch unexpected service exceptions and show feedback.

Validation:

- Trash/desktop tests: 29 passed, including owned temporary files, recursive folders, symlink-target preservation and failed-deletion metadata retention.
- GUI action tests: 3 passed, covering bulk deletion with dispatcher work and synchronous/asynchronous power-service exceptions. The dispatcher test does not establish physical desktop responsiveness under arbitrary disk stalls.
- Core tests: 384 passed, one opt-in live screenshot test skipped. Guard command success and failure are covered by a fake runner.
- `scripts/test-session-logout.sh`: passed with two owned fixture children ignoring SIGTERM; an unrelated owned process survived. No actual logout was requested.
- Native session installer fixture tests and Bash syntax checks passed. Root and Avalonia whitespace checks passed.
- Files, Notes and Settings rebuilt with zero warnings/errors. Native shell reload completed (22595 → 29548), preserving KWin (1042). Strict compositor preflight returned zero failures/warnings. The new shell startup log contains no action-handler exceptions; the bundled guard matches its source.

The user's Trash was not emptied, and their session was not logged out. Physical acceptance of both actions remains with the user.
