# Plugin broker protocol

## Status

Protocol version 2 is a fail-closed process-boundary preview. It supports a
health handshake and a read-only `probe-metadata` operation. The probe proves
that a freshly trusted and approved package still has the exact bytes and
capabilities authorized by the host. It does not load an assembly, inspect
managed types, activate a plugin, or execute a command.

The transport version remains 2 because this stage changes process containment,
not the JSON schema. The host creates the broker with its primary thread
suspended and permits inheritance of only the three redirected standard-stream
handles. It assigns the process to a Windows Job Object limited to one active
process and 256 MiB committed memory before resuming that thread. Closing the
last job handle terminates the broker. Before reading a request, the broker
disables legacy extension points, remote and low-integrity image loading, and
child-process creation. Failure to establish either side of this profile fails
closed.

## Transport

- One UTF-8 JSON object per line over redirected standard input/output.
- One request and one response per broker process.
- Maximum decoded frame length: 65,536 characters.
- The host closes standard input after sending its request.
- The host applies a two-second timeout. A Job Object closes on completion,
  failure, or cancellation and lets Windows terminate any broker process still
  alive.
- The broker exits `0` only for an accepted request and `2` for a rejected frame.
- Responses are accepted only when protocol version, request ID, process ID,
  operation-specific metadata, and exit code all match.

The protocol never carries file contents, environment values, launcher query
text, arbitrary command strings, persisted consent documents, or secrets.

## Health request

```json
{
  "protocolVersion": 2,
  "requestId": "32_character_lowercase_guid",
  "operation": "health",
  "grant": null
}
```

A health request containing a grant is rejected.

## Metadata probe request

Immediately before creating the request, the host:

1. rescans the bounded package directory;
2. revalidates Authenticode and online publisher revocation;
3. confirms the exact publisher and capability consent; and
4. creates a single-package grant valid for 15 seconds.

```json
{
  "protocolVersion": 2,
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
  }
}
```

The broker rejects grants with unknown capability bits, invalid IDs or hashes,
non-absolute paths, a lifetime over 30 seconds, future/expired timestamps,
missing or oversized files, directory traversal, reparse points, or a SHA-256
mismatch. It returns only normalized identity metadata and never returns a path.

## Accepted metadata response

```json
{
  "protocolVersion": 2,
  "requestId": "same_request_id",
  "accepted": true,
  "status": "Package metadata matched the short-lived capability grant; activation remains disabled.",
  "brokerProcessId": 12345,
  "metadata": {
    "pluginId": "example.publisher.plugin",
    "assemblySha256": "64_HEXADECIMAL_CHARACTERS",
    "publisherCertificateSha256": "64_HEXADECIMAL_CHARACTERS",
    "grantedCapabilities": 1
  }
}
```

The client compares every metadata field to its request. A mismatch, rejection,
timeout, nonzero exit, or unexpected process ID fails closed.

## Remaining activation blockers

`probe-metadata` is not an activation token and does not make persisted consent
safe to send to arbitrary broker instances. Before code execution is added, the
broker still needs:

- dependency and native-library containment;
- a broker-authenticated, single-use activation channel;
- bounded command/result DTOs with no delegate or shell string;
- broker crash accounting and automatic quarantine;
- packaging and signing rules for the broker executable; and
- adversarial tests against managed and native dependency escape.
