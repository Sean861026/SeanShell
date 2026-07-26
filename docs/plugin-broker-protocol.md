# Plugin broker protocol

## Status

Protocol version 3 is a fail-closed, process-boundary preview. It supports a
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
per-launch key; it does not replace executable packaging and signing. A
same-user attacker able to replace the configured broker binary remains outside
this stage's guarantee.

The JSON protocol carries no key, file contents, environment values, launcher
query text, arbitrary command strings, or persisted consent documents.

## Health request

```json
{
  "protocolVersion": 3,
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
  "protocolVersion": 3,
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
    "expiresAtUtc": "2026-07-26T00:00:15+00:00"
  },
  "sessionId": "32_character_lowercase_guid",
  "nonce": "64_HEXADECIMAL_CHARACTERS",
  "authenticationTag": "64_HEXADECIMAL_CHARACTERS"
}
```

The broker rejects grants with unknown capability bits, invalid IDs or hashes,
non-absolute paths, a lifetime over 30 seconds, future/expired timestamps,
missing or oversized files, directory traversal, reparse points, or a SHA-256
mismatch. It returns only normalized identity metadata and never returns a path.

## Accepted response

```json
{
  "protocolVersion": 3,
  "requestId": "same_request_id",
  "accepted": true,
  "status": "Package metadata matched the short-lived capability grant; activation remains disabled.",
  "brokerProcessId": 12345,
  "metadata": {
    "pluginId": "example.publisher.plugin",
    "assemblySha256": "64_HEXADECIMAL_CHARACTERS",
    "publisherCertificateSha256": "64_HEXADECIMAL_CHARACTERS",
    "grantedCapabilities": 1
  },
  "sessionId": "same_session_id",
  "nonce": "same_nonce",
  "authenticationTag": "64_HEXADECIMAL_CHARACTERS"
}
```

The client first authenticates the response, then compares its envelope,
metadata, started-process PID, exit code, and deadline. Any mismatch fails
closed. A captured frame cannot authenticate in a later broker process because
that process receives a different random key.

## Remaining activation blockers

Before code execution is added, the broker still needs:

- dependency and native-library containment;
- bounded command/result DTOs with no delegate or shell string;
- broker crash accounting and automatic quarantine;
- packaging and signing rules for the broker executable; and
- adversarial tests against managed and native dependency escape.
