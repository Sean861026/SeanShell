# Plugin broker protocol

## Status

Protocol version 1 is a fail-closed process-boundary preview. It proves that the
host can start, identify, time-limit, and terminate a separate SeanShell broker.
It does not provide plugin discovery, assembly loading, dependency resolution,
activation, or command execution.

## Transport

- One UTF-8 JSON object per line over redirected standard input/output.
- One request and one response per broker process.
- Maximum decoded frame length: 65,536 characters.
- The host closes standard input after sending its request.
- The host applies a two-second handshake timeout and terminates the broker
  process tree if the exchange fails or is cancelled.
- The broker exits `0` only for an accepted request and `2` for a rejected frame.

The App never sends secrets, file contents, environment values, Plugin paths, or
user query text through this protocol.

## Version 1 request

```json
{
  "protocolVersion": 1,
  "requestId": "32_character_lowercase_guid",
  "operation": "health"
}
```

`health` is the only accepted operation. Operation matching is ordinal and
case-sensitive. Unknown operations receive a fixed rejection message that does
not echo untrusted input.

There is intentionally no field for an assembly path, type, method, argument,
capability token, or trust decision. Requests such as `activate` and
`load-assembly` cannot be represented as supported behavior.

## Version 1 response

```json
{
  "protocolVersion": 1,
  "requestId": "same_request_id",
  "accepted": true,
  "status": "Broker handshake ready; external activation is disabled.",
  "brokerProcessId": 12345
}
```

The host accepts a handshake only when the process exits successfully, the
protocol and request ID match, `accepted` is true, and `brokerProcessId` equals
the process it started. Every other outcome fails closed.

## Before activation can be added

The next protocol version must define:

- package revalidation immediately before broker launch;
- a short-lived, single-package capability grant rather than the persisted
  consent document itself;
- Windows process mitigation and resource policy;
- dependency and native-library containment;
- bounded command/result DTOs with no arbitrary delegate or shell string;
- broker crash accounting and automatic quarantine; and
- packaging/signing rules for the broker binary.

External activation remains blocked until those controls and representative
adversarial tests ship.
