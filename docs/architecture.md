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
- `SeanShell.Plugins` validates manifests and owns bounded plugin lifecycle,
  launcher queries, fault isolation, and diagnostics.
- projects under `plugins/` contain explicitly registered built-in implementations.

Dependencies point inward: App may depend on every module; Windows, Gaming, and
PluginContracts depend on Core; the plugin host depends on Core and contracts;
Core depends only on .NET. Plugins never receive the App service container or
direct access to UI internals.

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
- Plugin operations are asynchronous and cancellable. Initialization, command
  queries, suspend, resume, and disposal are bounded by host timeouts. A failed
  plugin transitions to a session-local faulted state while healthy plugins keep
  serving commands.
- Only built-in instances registered by the App composition root are accepted.
  Third-party discovery remains blocked until signing, user consent, revocation,
  and out-of-process isolation are implemented.
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

`GamingModeManager` combines two independent sources: a session-only manual
override and the active process matches produced by automatic detection. Effective
gaming mode remains active while either source is active. This prevents a game
exit from cancelling a manual override, and prevents disabling the manual toggle
from cancelling a still-running detected game.

## Deployment

The initial app uses single-project MSIX packaging and a debug identity generated
by the Windows App SDK tooling. Release packaging, signing, update channels, and
full-shell policy are deferred until the MVP has measured compatibility data.
