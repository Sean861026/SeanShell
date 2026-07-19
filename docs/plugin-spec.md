# Plugin specification

## Status

This is an MVP contract draft. Binary compatibility is not guaranteed before 1.0.
Only built-in plugins should be loaded until isolation and signing policy are ready.

## Contract

Plugins implement `ISeanShellPlugin` and provide a stable ID, display name,
initialization lifecycle, launcher commands, and suspend/resume hooks. All lifecycle
operations are asynchronous and cancellable.

Gaming mode calls `SuspendAsync` for optional providers and `ResumeAsync` after the
last detected game exits. Plugins must treat both operations as idempotent and must
not assume they run on the UI thread.

## Command rules

- A `ShellCommand.Id` must be stable and unique within the plugin.
- Titles are user-facing; subtitles describe impact or destination.
- Execution honors cancellation and returns errors rather than terminating the host.
- Elevation requires explicit user interaction.
- Plugins may not inject into processes, install drivers, intercept global input,
  disable Windows security, or hook graphics APIs.

## Planned loading model

Each plugin will include a manifest with its ID, semantic version, minimum host API,
entry assembly, declared capabilities, and publisher identity. Before third-party
loading ships, the host must enforce initialization timeouts, bounded command-query
latency, crash isolation, capability consent, and a per-plugin disable switch.
