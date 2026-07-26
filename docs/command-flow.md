# Command flow

## Startup

```text
Windows sign-in
  -> Explorer starts normally
  -> User or startup task launches SeanShell
  -> load startup-health.json
  -> previous pending startup: increment consecutive failure count
  -> --startup and three failures: ensure Explorer is running, then exit
  -> manual launch: always allow a recovery attempt
  -> write a new pending startup session
  -> App loads validated configuration
  -> Core state store is created
  -> Windows services and built-in providers start
  -> validate explicitly registered built-in plugin manifests
  -> initialize each plugin with an independent timeout
  -> failed plugin: record diagnostics and continue startup
  -> Dashboard and dock become visible
  -> 30 seconds alive: mark startup healthy and clear failure count
```

A normal main-window close clears the pending session immediately. Unexpected
termination during the first 30 seconds leaves it pending for the next launch.
SeanShell does not register an automatic-start entry yet; `--startup` is the
guarded contract for that later feature.

## Launcher query

```text
User opens launcher
  -> Alt+Space is delivered by RegisterHotKey (or dashboard button)
  -> launcher window centers on the active display
  -> search input receives keyboard focus
  -> query is normalized
  -> built-in providers run in parallel
  -> enabled plugins return ShellCommand records
  -> results are merged, ranked, and de-duplicated
  -> successful provider/ranking duration enters the bounded in-memory sample
  -> first successful show records the one-time show-to-usable duration
  -> user selects a command
  -> command executes with cancellation and audit logging
  -> launcher closes or displays a recoverable error
```

Start Menu shortcuts are indexed once per process and warmed after the dashboard
starts. The first launcher opening remains functional if indexing fails because
system commands are provided independently.

The 60 ms input debounce is intentionally excluded from search duration. Cancelled
and failed searches do not enter the sample, and no query text is retained.

Commands carry behavior rather than raw shell strings. Providers that intentionally
invoke a terminal must show the exact command and working directory before any
elevated action.

## Plugin launcher query

```text
Launcher query
  -> PluginHost selects active plugins with LauncherCommands capability
  -> query selected plugins concurrently with a 250 ms limit
  -> healthy result: merge ShellCommand records into Launcher ranking
  -> exception or timeout: mark only that plugin Faulted
  -> publish diagnostic update to Dashboard
  -> keep all other providers and plugins available
```

Faulted plugins remain isolated for the rest of the session. Restarting SeanShell
creates a fresh built-in plugin instance. User-disabled plugins remain disabled
across restarts and skip startup initialization.

## Plugin enable or disable

```text
User changes a plugin Enabled switch
  -> disable the switch while the operation is pending
  -> PluginHost serializes the lifecycle transition
  -> disable: suspend active plugin and remove its Launcher commands
  -> enable: initialize once or resume an initialized plugin
  -> Gaming Mode active: keep the enabled plugin suspended
  -> persist normalized disabled plugin IDs atomically
  -> save success: refresh diagnostics and report success
  -> save failure: restore the previous runtime state and report a warning
```

Unknown IDs are preserved in settings so temporarily unavailable built-in plugins
do not lose the user's choice. No third-party assemblies are discovered or loaded.

## External plugin candidate scan

```text
Dashboard loads
  -> enumerate immediate external package directories to a fixed limit
  -> read and validate each bounded plugin.json
  -> reject traversal, links, missing/oversized assemblies, and duplicate IDs
  -> hash the entry assembly
  -> Windows verifies Authenticode trust and whole-chain revocation
  -> classify unsigned / revoked / offline / expired / distrusted separately
  -> compare signer certificate fingerprint with manifest
  -> show diagnostic state
  -> stop (no assembly load, activation, or command registration)
```

The same bounded flow runs when the user selects **Recheck trust**. Revocation
unavailable fails closed and can never reach `ReadyForConsent`.

```text
User selects Approve capabilities
  -> show plugin, publisher, signer fingerprint, and exact capabilities
  -> user confirms
  -> atomically persist the bound consent decision
  -> save succeeds: update dashboard to Approved / execution blocked
  -> save fails: retain previous in-memory trust and show warning

User revokes one candidate or all stored consent
  -> atomically persist removal
  -> refresh dashboard
  -> external execution remains blocked in every state
```

## Plugin broker boundary checks

```text
Host creates a protocol-v2 health request
  -> CreateProcessW starts SeanShell.PluginBroker with CREATE_SUSPENDED
  -> inherit only redirected stdin / stdout / stderr handles
  -> assign broker to one-process / 256 MiB / kill-on-close Job Object
  -> ResumeThread only after successful Job assignment
  -> broker applies extension-point, image-load, and child-process mitigations
  -> write one bounded JSON frame
  -> broker accepts exact operation "health" without a grant
  -> unknown version/ID/operation: fixed rejection + exit 2
  -> health: return matching ID and broker PID + exit 0
  -> host verifies response belongs to the started process
  -> timeout/cancellation/mismatch: close Job Object and fail closed
```

```text
User-approved plugin ID
  -> host repeats bounded catalog and online publisher trust scan
  -> require exact publisher + capability consent
  -> issue package/hash/capability grant for 15 seconds
  -> launch a new broker process with operation "probe-metadata"
  -> broker validates grant lifetime and known capability bits
  -> broker rejects traversal, reparse points, missing/oversized DLL
  -> broker recomputes SHA-256 and compares it with the grant
  -> host matches response metadata, request ID, broker PID, and exit code
  -> stop (no Assembly.Load, reflection, activation, or command execution)
```

There is still no command flow from metadata probing to plugin activation.

## Git repository refresh

```text
Git plugin initialization or explicit Launcher refresh
  -> resolve any repository containing the App working/binary directory
  -> inspect configured common repository roots to a bounded depth
  -> skip reparse points, dependency folders, and inaccessible paths
  -> cap discovery at twelve repositories
  -> run cancellable git status processes concurrently
  -> parse branch, change count, and ahead/behind state
  -> atomically replace the cached repository snapshot
  -> next Launcher query returns folder, VS Code, and terminal commands
```

Normal Launcher queries never start Git processes. The cache changes only during
plugin initialization or the explicit **Refresh Git repositories** command.

## WSL distribution refresh and launch

```text
WSL plugin initialization or explicit Launcher refresh
  -> start wsl.exe --list --verbose without a command shell
  -> decode redirected UTF-16 output
  -> parse default marker, distribution name, state, and WSL version
  -> atomically replace the cached distribution snapshot
  -> next Launcher query returns shell and file commands

User selects a distribution
  -> "<name> WSL shell": start wsl.exe --distribution <exact name>
  -> "<name> WSL files": open \\wsl.localhost\<exact name>
  -> Windows/WSL starts the selected distribution if necessary
```

Normal Launcher queries never invoke `wsl.exe`. Destructive lifecycle and
registration commands are not represented as `ShellCommand` records.

## Docker container refresh and open

```text
Docker plugin initialization or explicit Launcher refresh
  -> start docker container ls --all --format "{{json .}}" without a command shell
  -> CLI missing: cache "Docker CLI unavailable"
  -> Engine offline: cache "Docker Engine unavailable"
  -> Engine online: parse container identity, image, state, status, and TCP ports
  -> atomically replace the cached snapshot
  -> next Launcher query returns logs and localhost-port commands

User selects a cached container
  -> "<name> Docker logs": open docker logs --tail 200 --follow <exact ID>
  -> "<name> Docker port <host>": open http://localhost:<host>
```

Normal Launcher queries never invoke Docker. Log arguments are passed directly to
`docker.exe`, not through `cmd.exe` or PowerShell. Start, stop, restart, exec,
remove, pull, and Compose commands are not represented as `ShellCommand` records.

## .NET workspace refresh and open

```text
.NET plugin initialization or explicit Launcher refresh
  -> inspect configured developer roots to a bounded depth
  -> skip build, dependency, reparse-point, and inaccessible directories
  -> cap discovery at 24 .sln, .slnx, and .csproj files
  -> parse project XML with DTD and external resolution disabled
  -> classify project type, target frameworks, test status, and run capability
  -> parse optional launchSettings and retain loopback HTTP/HTTPS URLs only
  -> atomically replace the cached workspace snapshot
  -> next Launcher query returns open, build, eligible test, and eligible run commands

User selects an explicit .NET action
  -> Launcher subtitle has already shown the complete dotnet command
  -> create Windows Terminal arguments without a command shell
  -> build: dotnet build <exact workspace path>
  -> test: dotnet test <exact solution/test-project path>
  -> run: dotnet run --project <exact runnable-project path>
  -> watch: dotnet watch run --project <exact runnable-project path>
  -> local URL: open validated loopback HTTP/HTTPS endpoint in default browser
```

Normal Launcher queries never read project files. Build, test, and run occur only
after user selection. Restore and project-mutation commands are not represented as
`ShellCommand` records.

## Launcher shortcut change

```text
User selects a reviewed shortcut preset
  -> release the current RegisterHotKey registration
  -> request the new registration from Windows
  -> success: activate it and persist settings atomically
  -> conflict: re-register the previous shortcut
  -> restore the ComboBox selection and show a recovery message
```

The dashboard button remains available even when no global shortcut can be
registered. SeanShell does not capture arbitrary keys or install an input hook.

## Dock window activation

```text
Dock refresh timer
  -> enumerate visible top-level application windows
  -> exclude cloaked, tool, owned, shell, and SeanShell windows
  -> display up to twelve window entries
  -> user selects an entry
  -> restore it when minimized
  -> request foreground activation
```

Windows foreground restrictions remain authoritative; SeanShell does not bypass
them with thread input attachment or injection.

## Multi-monitor dock and auto-hide

```text
SeanShell startup
  -> enumerate monitor work areas
  -> create one dock per monitor
  -> MainWindow captures one shared window snapshot every two seconds
  -> each Dock filters the shared snapshot by MonitorFromWindow result
  -> position dock above that monitor's taskbar
  -> pointer/focus leaves dock
  -> wait 900 ms
  -> collapse to visible edge indicator
  -> pointer enters indicator or keyboard focus returns
  -> restore full dock immediately

Windows sends WM_DISPLAYCHANGE
  -> restart a one-shot 500 ms debounce timer
  -> capture ordered monitor handles and work areas
  -> empty or equivalent topology: keep current Docks
  -> construct one replacement Dock per new monitor
  -> construction succeeds: shut down old Docks and activate replacements
  -> construction fails: close partial replacements and keep old Docks
  -> update dashboard display count
```

Focus within a dock cancels auto-hide. Gaming mode takes precedence and hides all
docks completely and stops the shared refresh loop. A topology change during
Gaming Mode prepares replacement Docks without showing them; the existing
normal-mode transition displays them later.

## Gaming mode

```text
Two-second process snapshot
  -> retain only processes matching normalized executable rules
  -> record bounded scan-duration and processor-time diagnostics
  -> GamingModeManager replaces its active detected-game set
  -> combine detected games with the manual session override
  -> either source active: ShellStateStore enters Gaming mode
  -> dashboard polling and optional plugins suspend
  -> dock hides and animations reduce
  -> last matched game exits and manual override is off
  -> complete and atomically save local compatibility-session summary
  -> ShellStateStore returns to Normal mode
  -> suspended providers resume
```

Plugin suspend and resume calls are idempotent host state transitions. A lifecycle
failure faults only the responsible plugin and never prevents the Dock or dashboard
from applying Gaming Mode.

Automatic detection is opt-in and rules are explicit. Manual mode always remains
available and is not persisted, preventing an accidental permanent gaming state.
Steam itself is not treated as a game unless the user adds it; rules should target
game executables to avoid keeping gaming mode active indefinitely.

## Recovery

```text
User runs tools/restore-explorer.ps1
  -> start explorer.exe when it is not running
  -> request a graceful SeanShell shutdown
  -> still running after two seconds: stop only SeanShell.App
  -> remove startup-health.json to reset automatic-start protection
```
