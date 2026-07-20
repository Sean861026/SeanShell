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
- `SeanShell.Windows` isolates Win32, process, registry, and shell integration.
- `SeanShell.Gaming` owns process rules and the policy for pausing optional work.
- `SeanShell.PluginContracts` is the small, versioned surface available to plugins.

Dependencies point inward: App may depend on every module; Windows, Gaming, and
PluginContracts depend on Core; Core depends only on .NET. Plugins never receive
the App service container or direct access to UI internals.

The M2 dock receives immutable `DesktopWindowSnapshot` records from a Windows-only
service. The UI never calls `EnumWindows` or activation APIs directly. System CPU
and memory sampling follows the same boundary and publishes a
`SystemMetricsSnapshot` to the dashboard.

`DisplayMonitorService` captures Win32 work areas as immutable monitor snapshots.
The App composition root creates one dock window per startup snapshot. Window
snapshots carry the nearest monitor handle, allowing each dock to filter locally
without opening or retaining process handles.

## Reliability boundaries

- Explorer remains the fallback throughout the MVP.
- Plugin operations are asynchronous, cancellable, and will gain timeouts and
  out-of-process isolation before third-party plugins are accepted.
- Configuration writes will be atomic and recover from a last-known-good copy.
- Gaming mode pauses polling and animations; it never disables security services,
  injects code, hooks rendering, or intercepts game input.
- The dock lists ordinary visible top-level application windows, excludes
  SeanShell itself, and uses `SetForegroundWindow` only after a user selection.
- A crash loop guard is required before any automatic startup feature ships.

## Configuration

`ShellSettingsStore` owns a versioned JSON document at
`%LOCALAPPDATA%\SeanShell\settings.json`. It writes a sibling temporary file,
flushes it to disk, and replaces the primary document while retaining
`settings.json.bak`. Invalid JSON, unknown schema versions, and unsupported
shortcut values never reach the UI: the store loads the backup or safe defaults
and returns a warning for the dashboard.

Schema version 2 persists Dock auto-hide, one of three reviewed Launcher
shortcuts, opt-in automatic game detection, and newline-delimited game process
rules. Version 1 settings migrate in memory without losing existing preferences.
Arbitrary key capture is intentionally excluded so SeanShell never needs a
keyboard hook. A shortcut change is committed only after `RegisterHotKey`
succeeds; failed registration restores the previously active shortcut.

`GamingModeManager` combines two independent sources: a session-only manual
override and the active process matches produced by automatic detection. Effective
gaming mode remains active while either source is active. This prevents a game
exit from cancelling a manual override, and prevents disabling the manual toggle
from cancelling a still-running detected game.

## Deployment

The initial app uses single-project MSIX packaging and a debug identity generated
by the Windows App SDK tooling. Release packaging, signing, update channels, and
full-shell policy are deferred until the MVP has measured compatibility data.
