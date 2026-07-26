# Plugin broker protocol

## Status

Protocol version 4 is a fail-closed, process-boundary preview. It supports a
health handshake and a read-only `probe-metadata` operation. It does not load an
assembly, inspect managed types, activate a plugin, or execute a command.

Each request uses a new broker process and a new random 256-bit session key. The
host creates the broker with its primary thread suspended and permits inheritance
of only stdin, stdout, stderr, and a private session-key pipe. It assigns the
process to a Windows Job Object limited to one active process and 256 MiB
committed memory, writes the key, closes its pipe, and only then resumes the
thread. The key is never present in JSON, on disk, or in an environment
variable. Both processes clear their key buffer before returning.

Before reading the key or request, the broker disables legacy extension points,
remote and low-integrity image loading, and child-process creation. Failure to
establish any part of this profile fails closed.

## Authenticated transport

- One UTF-8 JSON object per line over redirected standard input/output.
- One request and one response per process; every process receives a fresh key.
- Maximum decoded frame length: 65,536 characters.
- Request and response envelopes carry the same random session ID and 256-bit
  nonce.
- `authenticationTag` is HMAC-SHA-256 over the same JSON object with that field
  set to `null`.
- The broker verifies the request tag before using its operation, grant, or path.
- The host verifies the response tag before trusting status, metadata, or PID.
- Authentication tags use a fixed-time comparison.
- The host closes standard input after sending one request and applies a
  two-second deadline.
- The broker exits `0` only for an accepted request and `2` for a rejected frame.
- The Job Object terminates a broker left alive after completion, failure, or
  cancellation.

The inherited pipe proves that a response came from the one process given the
per-launch key. Production composition starts the exact current packaged
`SeanShell.App.exe`; there is no configurable broker binary. The manifest enables
Windows package-content integrity enforcement. A production certificate and
release-signing pipeline are still required before external execution ships.

The JSON protocol carries no key, file contents, environment values, launcher
query text, arbitrary command strings, or persisted consent documents.

## Health request

```json
{
  "protocolVersion": 4,
  "requestId": "32_character_lowercase_guid",
  "operation": "health",
  "grant": null,
  "sessionId": "32_character_lowercase_guid",
  "nonce": "64_HEXADECIMAL_CHARACTERS",
  "authenticationTag": "64_HEXADECIMAL_CHARACTERS"
}
```

A health request containing a grant is rejected.

## Metadata probe request

Immediately before creating the request, the host rescans the bounded package
directory, revalidates Authenticode and online publisher revocation, confirms
exact publisher/capability consent, and creates a grant valid for 15 seconds.

```json
{
  "protocolVersion": 4,
  "requestId": "32_character_lowercase_guid",
  "operation": "probe-metadata",
  "grant": {
    "pluginId": "example.publisher.plugin",
    "packageDirectoryPath": "absolute_package_directory",
    "entryAssemblyPath": "absolute_package_dll",
    "assemblySha256": "64_HEXADECIMAL_CHARACTERS",
    "publisherCertificateSha256": "64_HEXADECIMAL_CHARACTERS",
    "grantedCapabilities": 1,
    "issuedAtUtc": "2026-07-26T00:00:00+00:00",
    "expiresAtUtc": "2026-07-26T00:00:15+00:00",
    "dependencies": [
      {
        "relativePath": "lib/Example.Support.dll",
        "sha256": "64_HEXADECIMAL_CHARACTERS",
        "kind": "managed"
      }
    ]
  },
  "sessionId": "32_character_lowercase_guid",
  "nonce": "64_HEXADECIMAL_CHARACTERS",
  "authenticationTag": "64_HEXADECIMAL_CHARACTERS"
}
```

The broker rejects grants with unknown capability bits, invalid IDs or hashes,
non-absolute paths, a lifetime over 30 seconds, future/expired timestamps,
missing or oversized files, directory traversal, reparse points, or a SHA-256
mismatch. A dependency allowlist is limited to 32 canonical package-relative
DLL paths of at most 240 characters, 256 MiB per file, and 512 MiB total. Kinds
are `managed` or `native`. The broker rejects duplicates, the entry assembly
listed as its own dependency, traversal, reparse points, and hash changes. It
returns only normalized identity metadata and never returns a path.

## Accepted response

```json
{
  "protocolVersion": 4,
  "requestId": "same_request_id",
  "accepted": true,
  "status": "Package metadata matched the short-lived capability grant; activation remains disabled.",
  "brokerProcessId": 12345,
  "metadata": {
    "pluginId": "example.publisher.plugin",
    "assemblySha256": "64_HEXADECIMAL_CHARACTERS",
    "publisherCertificateSha256": "64_HEXADECIMAL_CHARACTERS",
    "grantedCapabilities": 1,
    "dependencyCount": 1,
    "dependencySetSha256": "64_HEXADECIMAL_CHARACTERS"
  },
  "sessionId": "same_session_id",
  "nonce": "same_nonce",
  "authenticationTag": "64_HEXADECIMAL_CHARACTERS"
}
```

The dependency-set digest is SHA-256 over a length-prefixed, path-normalized,
case-normalized sequence sorted by relative path. The client first authenticates
the response, then compares its envelope, metadata, dependency count/digest,
started-process PID, exit code, and deadline. Any mismatch fails
closed. A captured frame cannot authenticate in a later broker process because
that process receives a different random key.

## Crash accounting and quarantine

The host records broker transport timeouts, truncated frames, malformed or
unauthenticated responses, and authenticated rejections against the selected
plugin ID. Three failures within ten minutes quarantine that plugin for thirty
minutes. The history is written atomically with a last-known-good recovery copy,
and an unreadable history with no recovery copy blocks every external probe.

A successful metadata probe removes that plugin's failure window. User
cancellation, a failed catalog/trust recheck, and an unavailable broker
installation do not count as plugin-caused failures. Quarantine is separate from
publisher consent and never authorizes execution.

## Remaining activation blockers

`SeanShell.PluginBroker.Runtime` now contains a collectible
`PluginDependencyLoadContext`, but no protocol operation constructs it. The
context repeats allowlist bounds, path/hash/reparse checks, rejects dependency
names that collide with framework or explicitly shared assemblies, and throws
instead of returning `null` for undeclared managed or native names. Managed
assemblies load from the same open stream whose SHA-256 was checked, avoiding
an unlocked path between verification and managed loading.

The protocol assembly also reserves strict data-only command DTOs for a future
activation version:

- query: at most 256 display characters and 32 requested results;
- descriptor set: at most 32 unique opaque IDs, bounded title/subtitle, at most
  eight keywords each, and 8,192 aggregate characters;
- invocation: one opaque command ID plus the SHA-256 of the exact descriptor
  set; and
- result: `succeeded`, `failed`, or `cancelled` plus at most 512 display
  characters.

Their codec rejects unknown JSON members, control characters, invalid IDs,
duplicate IDs/keywords, oversized frames, and invalid digests. There is no
delegate, executable, path, argument, URL, or shell field. These types do not
change protocol v4 and no request or response currently carries them.

Before code execution is added, the broker still needs:

- a broker-owned staging and cleanup design for verified native bytes before
  `LoadUnmanagedDllFromPath` is enabled;
- production certificate management and release-signing policy; and
- an explicit activation lifecycle with trusted entry-type selection,
  per-command deadlines, and crash accounting.
