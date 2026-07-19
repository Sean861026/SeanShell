# Data flow

## Shell state

```text
Windows process snapshot
  -> ProcessSnapshot[]
  -> GameDetector
  -> GamingModeManager
  -> ShellStateStore
  -> ShellStateChanged event
  -> App, dashboard providers, dock, and plugins
```

`ShellState` is immutable. The store serializes transitions and emits an event only
when the mode changes, preventing repeated process scans from causing unnecessary UI
updates.

## Launcher results

```text
query text
  -> normalization
  -> installed-app provider (cached Start Menu index)
  -> system-command provider
  -> repository provider
  -> plugin providers
  -> ShellCommand[]
  -> ranking and de-duplication
  -> launcher view model
```

Provider calls receive a cancellation token. Results include stable IDs so ranking
and telemetry do not need to retain user query contents.

Ranking prefers exact title, title prefix, word prefix, substring, keywords, and
finally ordered-character subsequences. A failed provider contributes no results
and does not prevent healthy providers from serving the launcher.

## Dock and dashboard

```text
EnumWindows + DWM visibility
  -> DesktopWindowSnapshot[]
  -> dock view models
  -> horizontal dock list
  -> user selection
  -> SetForegroundWindow

GetSystemTimes + GlobalMemoryStatusEx
  -> SystemMetricsSnapshot
  -> dashboard CPU/RAM cards
```

Both surfaces refresh every two seconds in normal mode. Gaming mode stops their
timers and hides the dock. No process handles are retained, and no data is written
to disk.

## Configuration

The planned configuration model is a versioned JSON document stored under the
user's local application data. Secrets and OAuth tokens are not valid configuration
values; plugins must use Windows Credential Manager or an equivalent protected
store. Logs must omit command arguments, file contents, and credentials by default.
