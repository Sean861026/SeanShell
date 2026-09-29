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
provider that never completes can keep the action gate busy until shell restart;
this change does not invent a timeout that might claim a still-running action
was cancelled. Full-screen exclusive games may also refuse normal focus switching.

## Local validation, 2026-09-29

Release x64 on the development machine: clicked into the search field, searched
SeanShell and Vivaldi, observed the Vivaldi icon and result count, dismissed with
Escape, reopened with an empty query, and used Enter to open File Explorer.
The palette hid on normal completion; the test-created Explorer window was closed.
Delayed success/failure after reopen/shutdown is covered by controlled-task Core
tests, not a claim that every real plugin or exclusive-full-screen game was tested.
