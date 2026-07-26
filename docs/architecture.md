# Architecture

## Purpose

SeanShell is a modular developer workspace for Windows. The first releases run
beside Explorer and use supported Windows APIs. Full shell replacement is an
optional, late-stage deployment mode, not an MVP requirement.

## Operating modes

1. **Overlay mode (MVP):** Explorer owns the desktop, taskbar, tray, and shell
   services. SeanShell supplies its own launcher, dock, and dashboard windows.
2. **Companion shell mode:** Explorer remains available for shell services while
   selected Explorer surfaces are hidden.
3. **Full shell mode:** SeanShell becomes the configured shell only on supported
   Windows editions and only after recovery and compatibility gates pass.

## Components

- `SeanShell.App` is the WinUI 3 composition root. It contains views and binds
  platform services to feature modules.
- `SeanShell.Core` owns immutable models, shell state, and command abstractions.
- `SeanShell.Windows` isolates Win32, process, registry, shell, and Authenticode
  trust integration.
- `SeanShell.Gaming` owns process rules and the policy for pausing optional work.
- `SeanShell.PluginContracts` is the small, versioned surface available to plugins.
- `SeanShell.Plugins` validates manifests and owns bounded plugin lifecycle,
  launcher queries, fault isolation, and diagnostics.
- projects under `plugins/` contain explicitly registered built-in implementations.

Dependencies point inward: App may depend on every module; Gaming and
PluginContracts depend on Core; the plugin host depends on Core and contracts;
the Windows boundary depends on Core and the plugin host's narrow trust-verifier
contract. Core depends only on .NET. Plugins never receive the App service
container or direct access to UI internals.

The built-in Git plugin receives a small list of repository roots from the App
composition root. The App first walks upward from its current and binary
directories to resolve a containing repository, which keeps packaged build output
discoverable without scanning arbitrary working directories. Common developer
folders use breadth-first discovery capped at depth two and twelve repositories,
skip reparse points and build/dependency folders, and never scan all of the user's
Documents directory. Repository metadata is read by a
cancellable `git status` child process during initialization or an explicit
Launcher refresh. Launcher queries use the cached immutable snapshots.

The built-in .NET workspace plugin receives the same bounded developer roots. A
breadth-first scan is capped at depth four and 24 solution/project files, skips
build, dependency, reparse-point, and inaccessible directories, and never scans
the entire disk. Project inspection uses an XML reader with DTD processing and
external resolution disabled. Only the project SDK, target frameworks, selected
references, and the presence of nearby Razor component files are used to classify
the in-memory snapshot. Launcher queries never touch the filesystem.
`launchSettings.json` is optional and parsed with comments/trailing commas
enabled to match common .NET templates. Only absolute HTTP/HTTPS loopback URLs
are cached, capped at eight per project; external hosts and non-web schemes are
discarded.

The M2 dock receives immutable `DesktopWindowSnapshot` records from a Windows-only
service. The UI never calls `EnumWindows` or activation APIs directly. System CPU
and memory sampling follows the same boundary and publishes a
`SystemMetricsSnapshot` to the dashboard. MainWindow owns one two-second Dock
refresh loop, captures one immutable window snapshot, and distributes it to every
monitor-local Dock. Docks filter that shared snapshot by monitor and never start
their own enumeration task.

`LauncherPerformanceMonitor` is a Core-owned, thread-safe session diagnostic. It
records the first successful show-to-usable duration once and keeps a bounded
window of the 50 most recent successful provider/ranking durations. Cancelled and
failed searches are not samples. The monitor retains durations only, never query
text, command results, or user identifiers, and does not write telemetry to disk.

`DisplayMonitorService` captures Win32 work areas as immutable monitor snapshots.
The App composition root creates one dock window per snapshot. A window subclass
observes `WM_DISPLAYCHANGE` and schedules a debounced topology capture on the UI
dispatcher. Equivalent snapshots are ignored. When handles, work areas, or monitor
membership change, the App constructs every replacement Dock before shutting down
the previous set. A failed or empty capture leaves the existing set active. Window
snapshots carry the nearest monitor handle, allowing each dock to filter locally
without opening or retaining process handles.

## Reliability boundaries

- Explorer remains the fallback throughout the MVP.
- Plugin operations are asynchronous and cancellable. Initialization, command
  queries, suspend, resume, and disposal are bounded by host timeouts. A failed
  plugin transitions to a session-local faulted state while healthy plugins keep
  serving commands.
- Git integration is read-only. It may open a repository in Explorer, VS Code, or
  Windows Terminal, but never runs pull, commit, checkout, reset, or clean.
- WSL integration caches the output of `wsl.exe --list --verbose`. Enumeration
  runs only during initialization or an explicit refresh. The plugin may start a
  selected distribution by opening its shell or files, but exposes no terminate,
  unregister, import, export, default-change, or arbitrary command actions.
  Docker Desktop's internal distributions are filtered from Launcher commands.
- Docker integration caches `docker container ls --all` JSON lines during plugin
  initialization or an explicit refresh. A missing CLI or offline Engine is a
  recoverable availability state, not a plugin fault. Container commands may open
  a running container's published localhost TCP port or follow logs in a separate
  console, but expose no start, stop, restart, exec, remove, pull, or Compose
  mutations.
- .NET workspace integration only opens a cached solution/project path, its
  containing folder in VS Code, or Windows Terminal, or starts an exact visible
  `dotnet build`, `dotnet test`, or `dotnet run --project` command after the user
  selects it. Runnable projects may also start `dotnet watch run` for hot reload
  or open a validated loopback launch-profile URL. Arguments are passed directly
  without a command shell. Package restore and project mutation commands are not
  exposed.
- Only built-in instances registered by the App composition root are accepted.
  The external catalog scans at most 32 immediate package directories and reports
  manifest, containment, content-hash, Authenticode, and publisher-fingerprint
  diagnostics. It never loads candidate code. External execution remains blocked
  after consent until publisher revocation policy and out-of-process isolation
  are implemented.
- Configuration writes will be atomic and recover from a last-known-good copy.
- Gaming mode pauses polling and animations; it never disables security services,
  injects code, hooks rendering, or intercepts game input.
- The dock lists ordinary visible top-level application windows, excludes
  SeanShell itself, and uses `SetForegroundWindow` only after a user selection.
- Automatic startup is not registered by SeanShell yet. The reserved `--startup`
  mode uses a persistent crash-loop guard and exits after three consecutive
  incomplete startup windows. Manual launches remain available for recovery.

## Configuration

`ShellSettingsStore` owns a versioned JSON document at
`%LOCALAPPDATA%\SeanShell\settings.json`. It writes a sibling temporary file,
flushes it to disk, and replaces the primary document while retaining
`settings.json.bak`. Invalid JSON, unknown schema versions, and unsupported
shortcut values never reach the UI: the store loads the backup or safe defaults
and returns a warning for the dashboard.

Schema version 3 persists Dock auto-hide, one of three reviewed Launcher
shortcuts, opt-in automatic game detection, newline-delimited game process rules,
and normalized disabled plugin IDs. Version 1 and 2 settings migrate in memory
without losing existing preferences.
Arbitrary key capture is intentionally excluded so SeanShell never needs a
keyboard hook. A shortcut change is committed only after `RegisterHotKey`
succeeds; failed registration restores the previously active shortcut.

Plugin enablement is independent of Gaming Mode. Disabling a plugin suspends it
and removes its Launcher commands; disabling before startup skips initialization.
Enabling while Gaming Mode is active initializes or resumes the plugin and then
keeps it suspended until normal mode returns. A failed settings write rolls the
runtime state back so persisted and visible state remain consistent.

External consent is independent from built-in enablement. A schema-1
`plugin-trust.json` document binds the package ID, signer certificate SHA-256,
exact capability flags, and UTC grant time. The trust manager saves a complete
replacement document before changing its in-memory snapshot, so a failed write
cannot create session-only approval. Primary corruption recovers from `.bak`;
unrecoverable data fails closed to no approvals. Removing a package does not
remove the decision, so the dashboard also provides a package-independent
revoke-all action.

`StartupCrashLoopGuard` owns a small schema-1 `startup-health.json` document under
the App's local SeanShell data directory. In an MSIX launch, Windows redirects
this to the package's `LocalCache\Local\SeanShell` directory; unpackaged launches
use `%LOCALAPPDATA%\SeanShell`. A new session is pending until the main window
closes normally or the process remains alive for 30 seconds. A later automatic
launch treats an earlier pending session as one startup failure. After three
consecutive failures, only `--startup` launches are blocked; manual launch is
always allowed and a healthy manual session resets the counter. If health state
cannot be persisted, automatic launch fails closed while manual recovery remains
available.

When an automatic launch is blocked, the Windows boundary ensures Explorer is
running before SeanShell exits. `tools/restore-explorer.ps1` provides the same
recovery path, requests a graceful close, stops only a still-running
`SeanShell.App` process after two seconds, and removes both packaged and
unpackaged health-document locations. The guard does not change Winlogon,
Scheduled Tasks, registry Run keys, or the configured Windows shell.

`GamingModeManager` combines two independent sources: a session-only manual
override and the active process matches produced by automatic detection. Effective
gaming mode remains active while either source is active. This prevents a game
exit from cancelling a manual override, and prevents disabling the manual toggle
from cancelling a still-running detected game.

Automatic detection takes one disposable process-list pass every two seconds and
retains snapshots only for configured executable names. Its performance monitor
keeps at most 60 samples and exposes scan P95 plus estimated detector CPU use to
the dashboard. The estimate is diagnostic evidence for representative sessions,
not a system-wide profiler.

`GamingSessionRecorder` observes detected-process transitions independently from
the manual Gaming Mode override. It starts a session only when at least one
configured executable is detected, completes it after the final match exits, and
retains the 20 newest records through an atomic, backup-backed schema-1 JSON
store. Records remain local and contain process names and compatibility metadata,
not game content or process handles.

## Deployment

The initial app uses single-project MSIX packaging and a debug identity generated
by the Windows App SDK tooling. Release packaging, signing, update channels, and
full-shell policy are deferred until the MVP has measured compatibility data.
