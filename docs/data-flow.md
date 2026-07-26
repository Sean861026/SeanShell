# Data flow

## Shell state

```text
Windows process snapshot
  -> configured-name filter
  -> matching ProcessSnapshot[]
  -> 60-sample performance diagnostics
  -> GameDetector
  -> detected process ID/name set
  -> GamingModeManager + manual override
  -> GamingSessionRecorder
  -> local schema-1 history (maximum 20 completed detected sessions)
  -> ShellStateStore
  -> ShellStateChanged event
  -> App, dashboard providers, dock, and plugins
```

`ShellState` is immutable. The store serializes transitions and emits an event only
when the mode changes, preventing repeated process scans from causing unnecessary UI
updates.

`ProcessCatalog` disposes every temporary `Process` object immediately, skips
processes that exit or become inaccessible during enumeration, and creates
snapshots only for configured names. The detector runs every two seconds even in
gaming mode because it is the mechanism that restores normal mode; dashboard and
Dock polling remain suspended. A bounded monitor records the last 60 scan
durations and SeanShell processor-time deltas for in-app P95 and estimated-CPU
diagnostics.

Manual-only Gaming Mode does not create compatibility evidence. When detected
processes transition from none to one or more, the session recorder captures the
start time and resets detector diagnostics for a session-local sample window.
After the last match exits, the recorder stores executable names, duration,
Windows and SeanShell versions, and the final detector metrics. The JSON document
uses a temporary file, write-through flush, replacement, and recovery backup.

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

## Launcher performance

```text
successful show/search Stopwatch duration
  -> LauncherPerformanceMonitor
  -> first usable duration + latest 50 successful search durations
  -> last duration + nearest-rank P95 snapshot
  -> Launcher dashboard card
```

Cancelled and failed operations are excluded. Only durations and a bounded sample
count are retained in process memory; query text and command results never enter
this flow.

## Plugin lifecycle and diagnostics

```text
App-owned built-in registration
  -> PluginManifest validation
  -> PluginHost runtime state
  -> bounded Initialize / Query / Suspend / Resume
  -> PluginDiagnostic snapshot
  -> DiagnosticsChanged event
  -> dashboard Plugin list

ShellStateChanged
  -> Gaming: suspend Active plugins
  -> Normal: resume Suspended plugins

Dashboard Enabled switch
  -> PluginHost SetEnabledAsync
  -> initialize/resume or suspend
  -> normalized disabled plugin ID set
  -> atomic settings write
  -> refreshed PluginDiagnostic snapshot
```

Diagnostics contain plugin identity, declared capabilities, state, last operation,
duration, and exception type/message. They do not contain Launcher queries,
command arguments, environment values, file contents, or credentials.

## External plugin candidate diagnostics

```text
%LOCALAPPDATA%\SeanShell\plugins (immediate children, maximum 32)
  -> bounded plugin.json read (maximum 64 KiB)
  -> schema, capability, and duplicate-ID validation
  -> canonical package-contained DLL path with no reparse points
  -> bounded entry-assembly SHA-256 hash
  -> Windows Authenticode chain and online revocation verification
  -> structured trust state + local verification timestamp
  -> signer certificate SHA-256 comparison
  -> immutable diagnostic candidates
  -> dashboard External plugin candidates list
```

The flow ends at diagnostics. Candidate assembly bytes, manifest contents, and
local paths are not sent to a plugin host or persisted by SeanShell.
Revoked and unreachable-revocation states both fail closed, while remaining
distinct in the dashboard so the user can diagnose a permanent denial versus a
retryable certificate-service outage.

```text
ReadyForConsent candidate + explicit dashboard confirmation
  -> bind plugin ID + signer SHA-256 + exact capability flags + grant UTC
  -> validate complete schema-1 trust document
  -> write plugin-trust.json.tmp and flush to disk
  -> atomically replace plugin-trust.json and retain .bak
  -> update in-memory consent snapshot only after save succeeds

Candidate-specific revoke or package-independent revoke-all
  -> remove matching decision(s)
  -> same atomic save boundary
  -> refresh dashboard consent state
```

Consent is local policy data, not an activation command. No candidate path or
assembly hash is placed in the trust document, and no external code enters the
host.

## Plugin broker health handshake

```text
PluginBrokerClient
  -> create protocol-v1 health request + random request ID
  -> start exact SeanShell.PluginBroker executable without a command shell
  -> send one bounded JSON frame and close stdin
  -> broker validates frame size, version, request ID, and exact operation
  -> broker returns its PID + matching request ID, then exits
  -> host validates response, process ID, exit code, and two-second deadline
  -> any mismatch/failure: terminate broker process tree and fail closed
```

Protocol v1 carries no candidate, path, assembly, type, capability token, or
activation operation. The handshake is not connected to `PluginHost`.

## Dock and dashboard

```text
EnumWindows + DWM visibility
  -> DesktopWindowSnapshot[]
  -> one MainWindow-owned shared snapshot
  -> monitor filter per Dock
  -> dock view models and horizontal Dock lists
  -> user selection
  -> SetForegroundWindow

GetSystemTimes + GlobalMemoryStatusEx
  -> SystemMetricsSnapshot
  -> dashboard CPU/RAM cards
```

The dashboard and one App-owned Dock loop refresh every two seconds in normal
mode. Gaming mode stops their timers and hides every Dock. No process handles are
retained, and no data is written to disk.

```text
EnumDisplayMonitors + monitor work areas
  -> DisplayMonitorSnapshot[]
  -> one DockWindow per monitor

WM_DISPLAYCHANGE
  -> 500 ms UI-thread debounce
  -> new DisplayMonitorSnapshot[]
  -> DisplayTopologyComparer
  -> changed topology: replacement DockWindow set
  -> dashboard display count

MonitorFromWindow
  -> DesktopWindowSnapshot.MonitorHandle
  -> monitor-local dock filtering
```

Auto-hide is a persistent UI preference. A collapsed dock retains a visible edge
indicator; pointer entry or routed keyboard focus expands it.

## Git repository snapshots

```text
App-configured repository roots
  -> bounded GitRepositoryDiscovery
  -> repository paths
  -> git status --porcelain --branch
  -> GitRepositorySnapshot[]
  -> cached plugin ShellCommand records
  -> Launcher ranking
```

Snapshots contain only repository path, display name, branch, working-tree change
count, and ahead/behind text. Git output is not persisted or logged. Repository
mutation, credential access, remote calls, and unbounded filesystem scanning are
outside this plugin.

## WSL distribution snapshots

```text
wsl.exe --list --verbose
  -> UTF-16 text
  -> WslDistributionParser
  -> WslDistributionSnapshot[]
  -> cached plugin ShellCommand records
  -> Launcher ranking
```

Snapshots contain only distribution name, default marker, running state, and WSL
version. They are held in memory and refreshed only by initialization or explicit
user action. No Linux command, environment value, filesystem content, or
distribution configuration is read or persisted.

## Docker container snapshots

```text
docker container ls --all --format "{{json .}}"
  -> one JSON object per line
  -> DockerContainerParser
  -> DockerContainerSnapshot[]
  -> cached availability and container state
  -> plugin ShellCommand records
  -> Launcher ranking
```

Snapshots contain container ID, name, image, state, status text, and published TCP
port mappings. They remain in memory and refresh only during initialization or an
explicit user action. Docker stderr, configuration, credentials, environment
values, mounts, labels, and file contents are not retained or shown.

## .NET workspace snapshots

```text
App-configured developer roots
  -> bounded DotNetWorkspaceDiscovery
  -> .sln / .slnx / .csproj paths
  -> safe project metadata inspection
  -> DotNetWorkspaceSnapshot[]
  -> cached plugin ShellCommand records
  -> Launcher ranking
```

Snapshots contain a local path, display name, classified project type, target
framework names, a solution marker, test-project status, run capability, and up
to eight validated loopback launch-profile URLs. File contents are not retained
or logged. The cache changes only during initialization or an explicit refresh.

Explicit build, test, and run selections flow from immutable snapshot metadata to
`ProcessStartInfo.ArgumentList`. SeanShell never constructs a `cmd.exe` or
PowerShell command string.

`launchSettings.json` URLs pass through absolute-URI, HTTP/HTTPS, and loopback
checks before entering a snapshot. External hosts and non-web schemes never become
Launcher commands.

## Configuration

```text
SeanShell startup
  -> load %LOCALAPPDATA%\SeanShell\settings.json
  -> validate schema and shortcut value
  -> invalid primary: load settings.json.bak
  -> invalid backup: use safe defaults and show warning
  -> apply Dock auto-hide and register Launcher shortcut
  -> migrate schema v1 or v2 to v3 in memory
  -> construct PluginHost with persisted disabled plugin IDs
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

## Startup health

```text
App launch mode + local startup-health.json
  -> StartupCrashLoopGuard
  -> previous pending session increments consecutive failures
  -> manual launch: create pending session
  -> automatic launch below threshold: create pending session
  -> automatic launch at threshold: ensure Explorer, then exit
  -> 30 seconds alive or clean close: clear pending/failures/disabled state
```

The schema-1 document contains only a generated session ID, pending flag,
consecutive failure count, and automatic-start disabled flag. It contains no
command line, user identifier, path history, or crash contents. Writes use a
sibling temporary file with write-through semantics. An unavailable health store
blocks automatic launch but does not block manual recovery.
