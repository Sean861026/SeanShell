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

### Runtime isolation follow-up, 2026-11-01

A separate minimal WinUI probe on the same SDC41A3 panel removed SeanShell's
styles, plugins, native icons, and services. With SDK 2.3.1 in framework-dependent
mode, the process loaded the installed Windows App Runtime 2.6.4. Text corruption
reappeared after a button click, while accessibility text and an app-owned
`RenderTargetBitmap` of its XAML tree remained correct. This narrows the evidence
to the presentation/capture path, not an incorrect search result string; it does
not prove whether the physical display and window-capture pipeline are affected
identically or establish a particular upstream defect.

The same probe bundling stable SDK/runtime 2.3.1 passed two button clicks, text
focus, and mixed Chinese/English input. A separate 2.5.4-experimental bundled
probe also passed this limited smoke test, but the production change uses the
verified **stable** runtime, not the experimental package. Loaded DLL paths and
SHA256 hashes were checked because these binaries expose the same FileVersion.

The complete SeanShell source at `a64000e`, built into isolated output with
`WindowsAppSDKSelfContained=true`, loaded its bundled WinUI DLL. Notebook-only
inspection passed Dock expansion, opening Launcher, clicking the search field,
typing Vivaldi, and displaying its icon and single result. The calendar remained
open across a later observation, quick settings remained readable, and the Dock
menu's Exit SeanShell closed the process with a CleanExit journal event. No
Wi-Fi, audio, GPU, driver, or display preference was changed. Accessibility-tree
capture was unavailable for this full app, so these checks used screenshots and
observed caret focus; they are not accessibility acceptance.

A subsequent taskbar-replacement startup test **failed closed**: the preference
was cleared and both Explorer taskbars remained visible. At that stage, graceful
and unexpected-exit restoration from a genuinely hidden state were unverified.
The test-owned process and its guards exited, the original disabled preference
and normal debug-package registration were verified restored, and no merge was
performed. Typed failure diagnostics now distinguish guard readiness, rejected
visibility requests, visibility timeouts, and work-area reservation failures
without recording window handles, paths, query text, or error-message contents.

The preceding cleanup-only commit `bddf166` passed all 544 tests locally and in
Windows CI. This count predates the new diagnostics and bundled-runtime changes;
those require their own full verification and recovery acceptance.

### Recovery retest after native visibility correction, 2026-11-01

The typed journal identified `Hide / VisibilityTimedOut` affecting one of two
taskbars, not a guard handshake error. A separate read-only 100-ms sampler saw
the primary taskbar hide while the secondary remained visible throughout the
sampled interval; it cannot exclude transitions shorter than the sampling period.
Both were shown again by the fail-safe path.

Taskbar-only visibility was changed to asynchronous `SetWindowPos`, preserving
geometry, activation, and Z-order. The guard handshake, one-request-per-target
behavior, two-second verification bound, and fail-closed handling were retained.
Normal application minimize/restore still uses `ShowWindowAsync`.

The normal Release x64 output, with the bundled stable runtime, then passed:

- Two connected Explorer taskbars verified hidden, and still hidden at a later
  observation while the notebook Dock was expanded and its menu was open.
- **Graceful exit:** Exit SeanShell was selected on the notebook Dock. Main and
  guard processes exited, both native taskbars became visible, and the journal
  recorded CleanExit without a taskbar failure.
- **Unexpected exit:** a fresh launch again hid both taskbars. Only the exact
  test-owned main process was terminated; its independent guard was not killed.
  Both processes exited within 223 ms of the termination request, and the next
  native-state check confirmed both taskbars visible. This is one local sample,
  not a restoration-latency guarantee or a hung-Explorer test.

All screenshots and UI input remained on SDC41A3; cross-display restoration was
verified using read-only native visibility metadata, not external-monitor UI
review. The original disabled taskbar-replacement preference (including the
test-written backup) and normal debug-package registration were restored.
Explorer was never stopped, and GPU, driver, display, Wi-Fi, and audio settings
were unchanged. The new full test run passed **557/557**, including the bundled
production App executable broker path; Release x64 built with zero warnings or
errors. Long-running games, exclusive-full-screen focus restrictions, x86/ARM64,
and accessibility-tree behavior remain outside this local acceptance.
