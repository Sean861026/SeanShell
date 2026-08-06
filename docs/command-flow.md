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
Host creates a protocol-v4 health request + random 256-bit session key
  -> production composition resolves exact packaged SeanShell.App.exe
  -> CreateProcessW starts it with --plugin-broker and CREATE_SUSPENDED
  -> inherit only stdin / stdout / stderr + private key-pipe handles
  -> assign broker to one-process / 256 MiB / kill-on-close Job Object
  -> write key through private pipe and close host key handle
  -> ResumeThread only after successful Job assignment
  -> broker applies extension-point, image-load, and child-process mitigations
  -> broker reads exactly one key and host writes one HMAC-tagged JSON frame
  -> broker verifies request tag before interpreting operation or grant
  -> broker accepts exact operation "health" without a grant
  -> unknown version/ID/operation: fixed rejection + exit 2
  -> health: return tagged matching envelope and broker PID + exit 0
  -> host authenticates response, then verifies envelope/PID/exit code
  -> both processes clear key buffers; broker handles no second frame
  -> timeout/cancellation/mismatch: close Job Object and fail closed
```

Normal packaged launch enters the custom `Main`, does not match
`--plugin-broker`, initializes WinUI, and creates `App`. Broker launch matches
the exact mode before any WinUI/WinRT initialization and delegates to the shared
identity-free runtime.

```text
User-approved plugin ID
  -> quarantine history available and plugin not currently quarantined
  -> host repeats bounded catalog and online publisher trust scan
  -> require exact publisher + capability consent
  -> issue package/hash/capability grant for 15 seconds
  -> launch a new broker process with operation "probe-metadata"
  -> broker validates grant lifetime and known capability bits
  -> broker rejects traversal, reparse points, missing/oversized DLL
  -> broker recomputes SHA-256 and compares it with the grant
  -> validate at most 32 declared managed/native dependency DLLs
  -> reject traversal, duplicate paths, reparse points, size/hash changes
  -> return dependency count + canonical set digest without local paths
  -> host matches response metadata, request ID, broker PID, and exit code
  -> success: atomically clear this plugin's broker failure window
  -> counted failure: atomically increment this plugin's ten-minute window
  -> third counted failure: quarantine this plugin for thirty minutes
  -> cancellation/trust failure/missing broker: do not charge the plugin
  -> stop (no Assembly.Load, reflection, activation, or command execution)
```

There is still no command flow from metadata probing to plugin activation.

The future activation path has one implemented but disconnected boundary:

```text
Validated dependency grant
  -> construct collectible PluginDependencyLoadContext
  -> repeat count / canonical path / unique path / size / reparse / hash checks
  -> framework name: resolve only from the .NET runtime
  -> explicit shared-contract name: return only the trusted supplied Assembly
  -> declared managed name: hash bytes again, then LoadFromStream
  -> declared native name: resolve exact package path (loading still disabled)
  -> any other name or trusted-name collision: throw and stop
```

The future command exchange is also defined but disconnected:

```text
bounded query text + maximum results
  -> strict DTO validation and unknown-field rejection
  -> bounded display-only command descriptors
  -> canonical command-set SHA-256
  -> invocation contains only opaque command ID + set digest
  -> result contains only fixed outcome + bounded display message
  -> no protocol v4 operation consumes or produces these DTOs
```

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

## Dock keyboard focus

```text
User presses Ctrl+Alt+D (or the selected Dock preset)
  -> Windows delivers the independently registered WM_HOTKEY
  -> capture the current foreground window and its nearest monitor
  -> resolve that monitor index; fall back to primary, then first available
  -> select the matching monitor-local Dock
  -> capture the current foreground window handle
  -> stop its pending auto-hide timer
  -> expand and activate the Dock through the native window boundary
  -> place keyboard focus on the Launcher button
  -> preserve the captured application's active state while no live app is foreground
  -> standard WinUI Tab and arrow navigation continues through Dock controls

User presses Escape while the Dock owns keyboard focus
  -> consume the routed Dock key event
  -> restore and activate the captured foreground window if it remains valid
  -> immediately collapse the Dock when auto-hide is enabled
```

Changing the Dock shortcut follows the same register-first, persist-on-success,
rollback-on-conflict flow as the Launcher shortcut. It does not simulate input
or install a keyboard hook.

While the Dock owns keyboard focus, Left and Right Arrow use native XY focus
navigation and Tab cycles within the Dock. Enter or Space invokes the focused
control. Escape restores the foreground window captured by the Dock shortcut.

## Dock window activation

```text
Dock refresh timer
  -> enumerate visible top-level application windows
  -> exclude cloaked, tool, owned, shell, and SeanShell windows
  -> display up to twelve window entries
  -> user selects an entry
  -> query live foreground and minimized state
  -> active + visible: request minimize
  -> otherwise: restore when minimized and request foreground activation

User opens an entry context menu
  -> single-window entry: expose Activate and bounded window actions directly
  -> multi-window group: expose one title/state submenu per window
  -> native compact presenter retains arrow, Enter, and Escape handling
  -> selected visible window: offer Activate and Minimize
  -> selected minimized window: offer Activate and Restore
  -> selected toggle: perform the named bounded window operation
  -> selected Close window: post WM_CLOSE
  -> target application may close, cancel, or prompt for unsaved work
  -> explicit cached application identity: offer Open new instance
  -> multiple shortcut identities: require a native submenu selection
  -> execute the original cached shortcut command

User hovers a running application entry
  -> wait 450 ms to reject pointer transit
  -> calculate a bounded one-to-six-window preview grid
  -> create DWM thumbnail relationships into SeanShell's preview HWND
  -> render live source-window previews without capture or injection
  -> pointer enters preview: cancel pending dismissal
  -> select preview: restore and activate that exact window, then dismiss
  -> select close: post WM_CLOSE to that exact window, then dismiss
  -> pointer leaves Dock and preview: wait 320 ms, unregister thumbnails, hide
  -> Dock snapshot changes or shutdown begins: unregister and dismiss immediately

User middle-clicks a Dock application
  -> standalone pin: execute its exact cached application command
  -> running item with no reliable application candidate: do not launch
  -> running item with one candidate: open a new instance
  -> running item with multiple candidates: show a native selection flyout
  -> selected candidate: execute its exact cached application command

User Shift-clicks a Dock application
  -> standalone pin: execute its exact cached application command as a new instance
  -> running item: follow the same verified candidate resolution as middle-click
  -> no reliable candidate: retain the current windows and do not guess
  -> one candidate: open a new instance
  -> multiple candidates: require a native selection flyout

User Ctrl+Shift-clicks a Dock application
  -> require a cached application with an existing fully-qualified local .exe
  -> reject network paths, unresolved shortcuts, and non-executable targets
  -> one verified candidate: request an elevated new instance through Windows Shell
  -> multiple verified candidates: require a native selection flyout
  -> Windows owns the UAC consent boundary; cancellation is reported without retry
```

Windows foreground restrictions remain authoritative; SeanShell does not bypass
them with thread input attachment or injection. An application may reuse its
existing process or window when its own single-instance policy handles the
shortcut.

## Show desktop

```text
User selects Show desktop on any Dock
  -> ShowDesktopSession requests Windows Shell MinimizeAll
  -> success: every Dock changes to Restore windows
  -> failure: retain the previous state and surface a dashboard warning

User selects Restore windows
  -> ShowDesktopSession requests Windows Shell UndoMinimizeAll
  -> success: every Dock returns to Show desktop

Shared Dock snapshot detects another foreground application
  -> invalidate SeanShell's pending restore state
  -> every Dock returns to Show desktop
```

No keyboard input is simulated and no application process is injected.

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

## Companion Taskbar

```text
User enables Replace Windows taskbar
  -> persist the opt-in schema-v7 preference
  -> launch the exact packaged SeanShell executable in recovery-guard mode
  -> guard opens the main-process handle and confirms readiness over stdout
  -> enumerate Shell_TrayWnd and Shell_SecondaryTrayWnd windows
  -> hide every discovered taskbar and verify visibility
  -> start a two-second re-hide check for Explorer restarts

User disables replacement or closes SeanShell normally
  -> stop the re-hide check
  -> show and verify every Windows taskbar
  -> persist the disabled preference

SeanShell main process crashes or is forcibly terminated
  -> recovery guard observes the owner-process handle becoming signaled
  -> retry restoration up to five times
  -> exit without restarting SeanShell or Explorer
```

The guard must acknowledge readiness before any taskbar is hidden. Failure to
start the guard, discover a taskbar, or verify the requested visibility fails
safe by showing all taskbars and clearing the persisted replacement preference.

## Launcher and pinned Dock applications

```text
User selects the Launcher/Start button on any Dock
  -> MainWindow requests the existing Launcher window
  -> Launcher opens on the current display

User opens the Launcher/Start context menu and selects Open Dashboard
  -> show the existing MainWindow without creating another instance
  -> restore the window when minimized
  -> request foreground activation through the normal Windows boundary

SeanShell starts or rebuilds monitor-local Docks
  -> show each Dock without activating it
  -> initial manual launch: restore the Dashboard and request foreground activation
  -> display rebuild: preserve the current foreground window

Desktop-window snapshot changes
  -> filter taskbar-eligible windows to each monitor
  -> group matching process windows into one Dock item
  -> reserve fixed width for the Launcher and complete system region
  -> reserve one stable slot for every visible pinned and running item
  -> resize and recenter the Dock within the monitor work area

Running-window items exceed the bounded viewport
  -> hide the precision-dependent horizontal scrollbar
  -> show keyboard-focusable previous and next controls
  -> resolve enabled state from current and maximum horizontal offsets
  -> button or mouse-wheel request advances by a bounded viewport page
  -> disable scroll animation when reduced effects are active

User searches for an installed application and selects Pin
  -> require ShellCommandKind.Application
  -> verify the exact app ID against the cached Start Menu index
  -> add it to the ordered list capped at eight IDs
  -> atomically persist schema-v7 settings
  -> resolve available pinned commands from the existing index
  -> distribute the same ordered command list to every Dock

User opens a running application's Dock context menu
  -> compare its process name only with explicit cached shortcut identities
  -> no match: do not offer a pin action
  -> one match: offer Pin to Dock
  -> multiple matches: offer a native candidate submenu
  -> selected candidate follows the same verified persistence flow

User selects a pinned Dock application
  -> execute the original installed-application ShellCommand
  -> Windows Shell opens the exact indexed shortcut

User Shift-clicks a pinned Dock application
  -> request a new instance through the same exact indexed shortcut
  -> the target application remains authoritative when it enforces single-instance behavior

User Ctrl+Shift-clicks a pinned Dock application
  -> require the cached shortcut to expose an existing fully-qualified local .exe
  -> request elevation with the exact cached arguments and the Windows `runas` verb
  -> Windows displays UAC and remains authoritative for consent

User selects Unpin in Launcher, on a standalone pin, or on its running item
  -> remove the exact app ID
  -> atomically persist and refresh every Dock

User selects Move left or Move right on a pinned item
  -> reject a missing ID or an outer-boundary move
  -> swap the exact ID with its adjacent neighbor
  -> atomically persist the ordered ID list
  -> resolve and refresh every monitor-local Dock
```

Selecting a pin command does not execute the result. A missing Start Menu
shortcut is not launched or displayed and is not removed from settings, allowing
the pin to recover if the shortcut returns.

For a pinned Start Menu shortcut whose resolved target is an existing local
`.exe`, its context menu also exposes **Open file location** and **Run as
administrator**. URI, UNC, missing, and indirect targets never receive those
actions. Elevated launch preserves the shortcut arguments and remains an
explicit user action subject to the normal Windows UAC prompt.

## Clock and Windows system-area access

```text
MainWindow 15-second clock timer
  -> capture one local timestamp
  -> each Dock formats system short-time and short-date patterns

User opens a Dock clock
  -> set the native CalendarView display date to the latest clock timestamp
  -> place the flyout above the Dock without constraining it to the compact Dock root
  -> preserve built-in keyboard month navigation and today highlighting
  -> keep later clock ticks from resetting the month being viewed

User selects the Dock system-area button
  -> require active Companion Taskbar replacement
  -> remove each Dock AppBar work-area reservation
  -> show and verify primary and secondary Windows taskbars
  -> keep the recovery guard and persisted replacement preference active
  -> pause the two-second re-hide check
  -> user accesses Explorer notification and system-tray surfaces directly

User selects the button again
  -> hide and verify every Windows taskbar
  -> register a bottom AppBar reservation for each display
  -> resume the two-second re-hide check

Gaming Mode starts while the system area is revealed
  -> hide and verify every Windows taskbar
  -> clear the session-only reveal state
  -> ensure all Dock AppBar reservations are removed
  -> hide SeanShell Docks and suspend optional providers

User opens Dock Quick settings
  -> capture current network availability
  -> capture current native battery and AC-line state
  -> capture current default render-endpoint volume and mute state
  -> format visible status and an accessible summary in Core
  -> update Network, Sound, and Power controls
  -> place the flyout above the Dock without constraining it to the compact Dock root
  -> show the flyout without starting a background polling timer

User changes the output-volume Slider or mute toggle
  -> activate the current default Windows Core Audio render endpoint
  -> apply the explicit master-volume or mute request
  -> read back and display the resulting endpoint state
  -> release the endpoint COM objects

User right-clicks the Dock background
  -> open one native keyboard-accessible MenuFlyout
  -> offer SeanShell settings and Task Manager
  -> offer Windows taskbar and general Settings pages
  -> when replacement is active, offer the current show/resume system-area action
  -> keep Exit SeanShell separated as the final action

User selects Exit SeanShell
  -> close the owner MainWindow
  -> remove every Dock AppBar reservation
  -> restore and verify native Windows taskbars
  -> stop timers and shut down every Dock, Launcher, and plugin host
```

Failure of either visibility transition fails safe by restoring native taskbars,
disabling the replacement preference, and keeping Explorer active.

## Per-display work-area reservation

```text
Companion Taskbar replacement succeeds
  -> calculate scaled Dock height + vertical margin
  -> ABM_NEW for each Dock HWND
  -> ABM_QUERYPOS on that monitor's bottom edge
  -> restore the requested height against the approved bottom edge
  -> ABM_SETPOS + native window position + ABM_WINDOWPOSCHANGED
  -> return the Dock to its centered visual rectangle
  -> Windows publishes the reduced per-monitor work area

Replacement disabled / system area revealed / Gaming Mode / shutdown
  -> ABM_REMOVE for every registered Dock
  -> Windows restores the available monitor work area
```

The taskbar controller reissues a pending visibility transition every 50
milliseconds for at most two seconds. Final visibility, not the first
`ShowWindow` return value, determines success.

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
  -> explicitly show primary and secondary Windows taskbars
  -> remove startup-health.json to reset automatic-start protection
```
