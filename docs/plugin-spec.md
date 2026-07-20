# Plugin specification

## Status

This is an MVP contract draft. Binary compatibility is not guaranteed before 1.0.
The M4 preview accepts only built-in instances explicitly registered by the App
composition root. Directory scanning and arbitrary assembly loading are disabled.

## Manifest schema 1

Every registration pairs an `ISeanShellPlugin` instance with a `PluginManifest`:

- `SchemaVersion`: currently `1`.
- `Id`: stable ASCII letters, digits, dots, and hyphens; must match the instance.
- `Name`: user-facing name; must match the instance.
- `Version`: numeric semantic version such as `1.2.0`.
- `MinimumHostApiVersion`: must not exceed the host API version.
- `Publisher`: displayed in diagnostics.
- `Capabilities`: currently `LauncherCommands` and `BackgroundWork`.
- `IsBuiltIn`: must be `true` while external loading is disabled.

Registration rejects duplicate IDs, unknown schema/API versions, undeclared
capability bits, identity mismatches, and external plugins.

## Contract

Plugins implement `ISeanShellPlugin` and provide a stable ID, display name,
initialization lifecycle, launcher commands, and suspend/resume hooks. All lifecycle
operations are asynchronous and cancellable.

Gaming mode calls `SuspendAsync` for optional providers and `ResumeAsync` after the
last detected game exits. Plugins must treat both operations as idempotent and must
not assume they run on the UI thread.

## Host limits and failure policy

- Initialization timeout: 3 seconds.
- Launcher query timeout: 250 milliseconds.
- Suspend, resume, and disposal timeout: 2 seconds.
- A timeout or unhandled exception marks only that plugin faulted for the session.
- Faulted plugins are skipped by later Launcher and lifecycle operations.
- Cancellation from the Launcher user flow remains cancellation, not a plugin fault.

The dashboard exposes manifest identity, capabilities, state, last operation,
duration, and a recoverable error. Diagnostics must not include query text,
arguments, file contents, environment values, or secrets.

## Command rules

- A `ShellCommand.Id` must be stable and unique within the plugin.
- Titles are user-facing; subtitles describe impact or destination.
- Execution honors cancellation and returns errors rather than terminating the host.
- Elevation requires explicit user interaction.
- Plugins may not inject into processes, install drivers, intercept global input,
  disable Windows security, or hook graphics APIs.

## Planned external loading model

An external manifest will additionally declare its entry assembly and signed
publisher identity. Third-party loading remains blocked until capability consent,
signature verification and revocation, a persistent disable switch, and stronger
out-of-process crash isolation are implemented.

The current in-process timeout bounds how long SeanShell waits; it cannot forcibly
terminate synchronous plugin code that ignores cancellation. This is acceptable
only for reviewed built-in plugins and is the primary reason external loading stays
disabled.
