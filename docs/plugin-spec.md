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
  "schemaVersion": 2,
  "id": "example.publisher.plugin",
  "name": "Example plugin",
  "version": "0.1.0",
  "minimumHostApiVersion": 1,
  "publisher": "Example Publisher",
  "capabilities": ["LauncherCommands"],
  "entryAssembly": "Example.Plugin.dll",
  "entryType": "Example.Publisher.LauncherPlugin",
  "dependencies": [
    {
      "path": "lib/Example.Support.dll",
      "sha256": "64_HEXADECIMAL_CHARACTERS",
      "kind": "managed"
    }
  ],
  "publisherCertificateSha256": "64_HEXADECIMAL_CHARACTERS"
}
```

The entry assembly must be a non-empty DLL no larger than 256 MiB, remain inside
its package directory, and use no reparse-point path component. The catalog
computes its SHA-256 content hash, asks Windows to validate its Authenticode trust
chain and revocation status, then compares the signer's SHA-256 certificate
fingerprint with the manifest. Duplicate IDs are rejected.

External manifest schema 2 requires `entryType` to use the same bounded dotted
ASCII identifier contract as broker activation. Schema 1 remains readable for
diagnostic compatibility but cannot declare an activation entry type. Adding or
changing an entry type therefore requires both schema 2 and fresh consent.

`dependencies` is optional and contains at most 32 unique package-relative DLLs.
Each item has a path of at most 240 characters, a SHA-256 content identity, and
kind `managed` or `native`. Individual files are limited to 256 MiB and the set
to 512 MiB. The host rejects traversal, duplicate canonical paths, reparse
points, hash changes, untrusted signatures, and any signer certificate different
from the entry assembly publisher.

Revocation checking covers the complete certificate chain except the root and may
retrieve current revocation data from the network. Results are reported
separately as trusted, unsigned, revoked, revocation unavailable, expired,
explicitly distrusted, or otherwise untrusted. Revocation unavailable is not a
soft success: the candidate remains ineligible for consent until a later recheck
can confirm trust. The dashboard records the verification time in the current
diagnostic snapshot and provides an explicit **Recheck trust** action.

Passing these checks makes the package eligible for explicit consent. The
dashboard confirmation shows the package name, publisher, signer certificate
SHA-256 fingerprint, exact requested capabilities, and the schema-2 entry type
when present. A schema-2 decision is
stored separately in `%LOCALAPPDATA%\SeanShell\plugin-trust.json` and binds:

- the stable plugin ID;
- the signer certificate SHA-256 fingerprint;
- the exact granted capability flags; and
- the exact activation entry type, or `null` for a migrated schema-1 package; and
- the UTC grant time.

A new signer certificate or additional capability is not covered by an older
decision. A user may revoke a candidate's decision or clear every stored decision,
including approvals whose package directory no longer exists. Writes are atomic,
retain a `.bak`, and recover safely; an unreadable document means no approvals.
Schema-1 trust files migrate in memory to schema 2 with a null entry type, which
preserves metadata-probe consent but cannot satisfy activation validation.

Consent does not call `Assembly.Load`, instantiate a type, register a command, or
pass the candidate to `PluginHost`. Third-party loading remains blocked until the
broker implements capability-restricted activation and stronger out-of-process
crash isolation. The current built-in Enabled switch remains separate from
external consent. A future loader must revalidate the exact file and online
revocation state immediately before every brokered activation rather than
trusting an earlier diagnostic snapshot.

The exact packaged `SeanShell.App.exe` starts as a separate child process in
`--plugin-broker` mode and implements the version-4 health and metadata-probe
operations described in
[plugin-broker-protocol.md](plugin-broker-protocol.md). The host repeats trust
and consent validation before issuing a 15-second grant bound to the package
paths, assembly SHA-256, publisher certificate, exact capabilities, and the
dependency allowlist. The broker rechecks containment, reparse points, size,
lifetime, capability bits, and every file hash, then returns a path-free set
digest. A new random pipe-delivered session key authenticates both frames
for each one-shot process. The broker never receives the persisted consent
document and is not a plugin host: there is no type, activation, method, or
command payload.

The broker runtime includes a not-yet-wired collectible assembly load context
for the future activation path. It resolves only exact-hash dependencies from
the manifest, runtime framework assemblies, and an explicit set of shared
contract assemblies. An undeclared managed dependency cannot fall back to a
host DLL, and an undeclared native dependency cannot use the platform loader's
name search. Managed dependencies are loaded from the verified open stream.
Native loading remains disabled until those bytes can be staged under a
broker-owned lifecycle.

Future command exchange is also constrained in advance by the protocol
assembly's data-only DTOs. Query text, display descriptors, opaque command IDs,
descriptor-set digests, fixed outcomes, and short messages are bounded and
strictly decoded. The DTOs contain no `ShellCommand` delegate and no field for
an executable, arguments, URL, local path, or shell string. They remain
disconnected from protocol v4.

Future activation has a separate strict request contract. The selected entry
type must be a bounded dotted ASCII identifier, and requested capability bits
must be a non-empty subset of the fresh consent-backed grant. Nested, generic,
assembly-qualified, path-like, unknown-capability, and escalation-shaped values
are rejected before any future loader can receive them. This is contract work
only; the current broker still does not activate external code.

The future request must match the entry type carried by its short-lived grant
exactly. Consent and trust composition can therefore bind one explicit type
rather than permitting a request to select another type from the same signed
assembly. Current metadata probes leave the optional grant field empty.

Single-project MSIX exposes only the App executable. The broker runtime is a
UI-independent class library shared with a standalone console test harness.
Production App composition never accepts a configurable broker path, and the
manifest enables package-content integrity enforcement.

Broker health is persisted independently from consent. Three counted probe
failures inside ten minutes quarantine the plugin ID for thirty minutes, while a
successful probe clears the sequence. Corrupt health state without a valid
backup blocks probes, and neither quarantine nor its expiry grants permission to
load code.

The current in-process timeout bounds how long SeanShell waits; it cannot forcibly
terminate synchronous plugin code that ignores cancellation. This is acceptable
only for reviewed built-in plugins and is the primary reason external loading stays
disabled.
