# Launcher lifecycle validation

## Automated scope

`LauncherOperationLifetimeTests` exercises single-flight admission, hide/reopen
invalidation, repeated show, permanent shutdown, and duplicate/wrong-ticket
completion. Diagnostic journal tests verify the new Launcher failure categories
omit exception messages and data. These tests cover the Core policy; they do not
instantiate a WinUI window or prove every native/compositor crash is fixed.

## Interactive regression checks

Use a Release x64 build and the configured Launcher shortcut or Dock button.

1. Open Launcher, click the search field, type an installed application name,
   and verify results, icons, selection, and the result count remain functional.
2. Change the search rapidly, then press Escape and reopen. The new query/session
   must not acquire stale results, old errors, or an incorrectly cleared spinner.
3. Open a safe existing app/setting using Enter, then reopen Launcher. Normal
   command completion must hide only the session that dispatched it.
4. For a genuinely asynchronous command, repeat Enter/click while it is pending.
   Results must be disabled and show `Operation in progress...`; the search field
   and Escape remain usable. Hide/reopen while pending: its later success/error
   must not hide or overwrite the reopened palette. The gate re-enables when the
   operation finishes. Do not use destructive commands to provoke delays/errors.
5. Pin/unpin a test app once and verify Dock state; restore its original pin state.
   Concurrent command/pin actions share the same admission gate.
6. Exit SeanShell from the Dock background menu while Launcher is open. All shell
   windows must close, native taskbars recover, and no managed error should be
   recorded from a late search/action continuation.

The single-flight gate prevents overlapping dispatch, not future launches after
completion. Escape does not undo an already dispatched external command. A
provider that never completes is bounded for the four built-in read-only refresh
commands by `ShellCommandExecutor` (30 seconds). Timeout requests cancellation
and releases the gate without claiming the underlying work has stopped; the
provider's serialization gate remains held until its worker finishes. Interactive
app/build/test/run/log-follow commands have no automatic deadline. Full-screen
exclusive games may also refuse normal focus switching.

`ShellCommandExecutorTests`, `RefreshCommandPolicyTests`,
`DotNetWorkspaceRefreshTests`, and `BoundedProcessRunnerTests` cover timeout,
cancellation, blocked cancellation callbacks, late failures, snapshot retention,
and owned query-process cleanup. These are controlled regressions, not a claim
that every external tool or native window has been exercised live.

## Local validation, 2026-09-29

Release x64 on the development machine: clicked into the search field, searched
SeanShell and Vivaldi, observed the Vivaldi icon and result count, dismissed with
Escape, reopened with an empty query, and used Enter to open File Explorer.
The palette hid on normal completion; the test-created Explorer window was closed.
Delayed success/failure after reopen/shutdown is covered by controlled-task Core
tests, not a claim that every real plugin or exclusive-full-screen game was tested.

## Local validation, 2026-11-01

Release x64 build: 0 warnings/errors; all 540 automated tests passed, including
the production App executable broker path. Notebook-only inspection confirmed
the SDC41A3 panel before capturing the Dock and Launcher. Ctrl+Alt+D expanded
that Dock, its Launcher button opened the palette on the same panel, and a click
inside the search field left the process responsive. The diagnostic journal
contained startup/healthy events, with no new failure event during this check.

Visual acceptance remains **blocked**: text and glyphs were missing or corrupted
in the Dock and palette, and no accessibility tree was available. Do not infer
that command execution, focus switching, calendar, or quick settings passed.
Taskbar replacement was disabled in the persisted configuration; no live
hide/restore or graceful/forced-exit recovery result is claimed for this build.
The test-owned process was stopped without terminating Explorer or other apps.
GPU, driver, and display preferences were not changed during this check. Keep
the responsiveness change unmerged until the remaining interactive/recovery
checks can be completed safely.
