# Data flow

## Shell state

```text
Windows process snapshot
  -> ProcessSnapshot[]
  -> GameDetector
  -> GamingModeManager
  -> ShellStateStore
  -> ShellStateChanged event
  -> App, dashboard providers, dock, and plugins
```

`ShellState` is immutable. The store serializes transitions and emits an event only
when the mode changes, preventing repeated process scans from causing unnecessary UI
updates.

## Launcher results

```text
query text
  -> normalization
  -> installed-app provider
  -> system-command provider
  -> repository provider
  -> plugin providers
  -> ShellCommand[]
  -> ranking and de-duplication
  -> launcher view model
```

Provider calls receive a cancellation token. Results include stable IDs so ranking
and telemetry do not need to retain user query contents.

## Configuration

The planned configuration model is a versioned JSON document stored under the
user's local application data. Secrets and OAuth tokens are not valid configuration
values; plugins must use Windows Credential Manager or an equivalent protected
store. Logs must omit command arguments, file contents, and credentials by default.
