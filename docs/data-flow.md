# Data flow

## Shell state

```text
Windows process snapshot
  -> ProcessSnapshot[]
  -> GameDetector
  -> detected process ID/name set
  -> GamingModeManager + manual override
  -> ShellStateStore
  -> ShellStateChanged event
  -> App, dashboard providers, dock, and plugins
```

`ShellState` is immutable. The store serializes transitions and emits an event only
when the mode changes, preventing repeated process scans from causing unnecessary UI
updates.

`ProcessCatalog` disposes every temporary `Process` object immediately and skips
processes that exit or become inaccessible during enumeration. The detector runs
every two seconds even in gaming mode because it is the mechanism that restores
normal mode; dashboard and Dock polling remain suspended.

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

```text
EnumDisplayMonitors + monitor work areas
  -> DisplayMonitorSnapshot[]
  -> one DockWindow per monitor

MonitorFromWindow
  -> DesktopWindowSnapshot.MonitorHandle
  -> monitor-local dock filtering
```

Auto-hide is a persistent UI preference. A collapsed dock retains a visible edge
indicator; pointer entry or routed keyboard focus expands it.

## Configuration

```text
SeanShell startup
  -> load %LOCALAPPDATA%\SeanShell\settings.json
  -> validate schema and shortcut value
  -> invalid primary: load settings.json.bak
  -> invalid backup: use safe defaults and show warning
  -> apply Dock auto-hide and register Launcher shortcut
  -> migrate schema v1 to v2 in memory
  -> configure automatic game detection and normalized process rules

User changes a setting
  -> validate/register requested shortcut when applicable
  -> registration failure: restore previous shortcut and do not save
  -> write settings.json.tmp and flush to disk
  -> atomically replace settings.json and retain settings.json.bak
```

Secrets and OAuth tokens are not valid configuration values; plugins must use
Windows Credential Manager or an equivalent protected store. Logs must omit
command arguments, file contents, and credentials by default.
