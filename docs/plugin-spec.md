# Plugin specification

## Status

This is an MVP contract draft. Binary compatibility is not guaranteed before 1.0.
The M4 preview accepts only built-in instances explicitly registered by the App
composition root. A bounded external package catalog is diagnostic-only: it
inspects manifests, paths, hashes, and Authenticode trust but never loads an
external assembly.

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

The dashboard may disable a built-in plugin at runtime. Disabling suspends an
active plugin, excludes it from queries and later lifecycle broadcasts, and saves
its stable ID. A plugin disabled at startup is not initialized. Re-enabling calls
`InitializeAsync` only if that instance has never initialized; otherwise it calls
`ResumeAsync`. If Gaming Mode is active, the plugin remains suspended after being
enabled.

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

The built-in Git plugin is a reference implementation of a read-only provider. It
uses bounded discovery and cached status snapshots, starts `git` without a command
shell, and exposes only user-initiated open actions. Repository-changing commands
are intentionally excluded.

The built-in WSL plugin follows the same cached-provider model. Enumeration starts
`wsl.exe` directly and exposes only user-initiated shell and file-open actions.
Terminate, shutdown, unregister, import, export, default changes, and arbitrary
Linux command execution are intentionally excluded.

The built-in Docker plugin invokes the Docker CLI directly without a command
shell. An offline Engine remains a normal cached state so it cannot fault SeanShell
startup. Commands may follow a selected container's logs or open its published
localhost TCP ports. Container lifecycle changes, image pulls, `docker exec`, and
Compose operations are intentionally excluded.

## Planned external loading model

External candidates are immediate child directories under
`%LOCALAPPDATA%\SeanShell\plugins`. At most 32 directories are inspected per
scan. Each package may contain a `plugin.json` of at most 64 KiB:

```json
{
  "schemaVersion": 1,
  "id": "example.publisher.plugin",
  "name": "Example plugin",
  "version": "0.1.0",
  "minimumHostApiVersion": 1,
  "publisher": "Example Publisher",
  "capabilities": ["LauncherCommands"],
  "entryAssembly": "Example.Plugin.dll",
  "publisherCertificateSha256": "64_HEXADECIMAL_CHARACTERS"
}
```

The entry assembly must be a non-empty DLL no larger than 256 MiB, remain inside
its package directory, and use no reparse-point path component. The catalog
computes its SHA-256 content hash, asks Windows to validate its Authenticode trust
chain and revocation status, then compares the signer's SHA-256 certificate
fingerprint with the manifest. Duplicate IDs are rejected.

Passing these checks means only **ready for a future consent flow**. The catalog
does not call `Assembly.Load`, instantiate a type, register a command, or pass the
candidate to `PluginHost`. Third-party loading remains blocked until per-capability
user consent, persisted publisher trust and revocation policy, and stronger
out-of-process crash isolation are implemented. The current persistent switch
applies only to trusted built-in registrations. A future loader must revalidate
the exact file immediately before brokered execution rather than trusting an
earlier diagnostic snapshot.

The current in-process timeout bounds how long SeanShell waits; it cannot forcibly
terminate synchronous plugin code that ignores cancellation. This is acceptable
only for reviewed built-in plugins and is the primary reason external loading stays
disabled.
