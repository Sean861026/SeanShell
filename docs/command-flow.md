# Command flow

## Startup

```text
Windows sign-in
  -> Explorer starts normally
  -> User or startup task launches SeanShell
  -> App loads validated configuration
  -> Core state store is created
  -> Windows services and built-in providers start
  -> Plugin contracts initialize with cancellation and timeout
  -> Dashboard and dock become visible
```

## Launcher query

```text
User opens launcher
  -> query is normalized
  -> built-in providers run in parallel
  -> enabled plugins return ShellCommand records
  -> results are merged, ranked, and de-duplicated
  -> user selects a command
  -> command executes with cancellation and audit logging
  -> launcher closes or displays a recoverable error
```

Commands carry behavior rather than raw shell strings. Providers that intentionally
invoke a terminal must show the exact command and working directory before any
elevated action.

## Gaming mode

```text
Process snapshot changes
  -> GameDetector matches normalized executable rules
  -> GamingModeManager updates active game process IDs
  -> ShellStateStore enters Gaming mode
  -> dashboard polling and optional plugins suspend
  -> dock hides and animations reduce
  -> last matched game exits
  -> ShellStateStore returns to Normal mode
  -> suspended providers resume
```

Manual mode always remains available. Steam itself is not treated as a game;
rules target game executables to avoid keeping gaming mode active indefinitely.

## Recovery

```text
User runs tools/restore-explorer.ps1
  -> start explorer.exe when it is not running
  -> request a graceful SeanShell shutdown
```
