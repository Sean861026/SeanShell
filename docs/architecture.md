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

## Reliability boundaries

- Explorer remains the fallback throughout the MVP.
- Plugin operations are asynchronous, cancellable, and will gain timeouts and
  out-of-process isolation before third-party plugins are accepted.
- Configuration writes will be atomic and recover from a last-known-good copy.
- Gaming mode pauses polling and animations; it never disables security services,
  injects code, hooks rendering, or intercepts game input.
- A crash loop guard is required before any automatic startup feature ships.

## Deployment

The initial app uses single-project MSIX packaging and a debug identity generated
by the Windows App SDK tooling. Release packaging, signing, update channels, and
full-shell policy are deferred until the MVP has measured compatibility data.
