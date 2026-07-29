# SeanShell

A modular Windows developer shell focused on performance, productivity, and gaming compatibility.

SeanShell starts as a safe companion to Windows Explorer. Its opt-in
**Companion Taskbar** mode hides the Windows taskbars and lets the SeanShell
docks carry window switching on every display, while Explorer continues to own
desktop and shell services. SeanShell does **not** replace `explorer.exe`, modify
Winlogon, inject into other processes, or hook graphics and input APIs. A
separate recovery guard and the recovery script preserve a direct path back to
the standard Windows desktop.

## Technology baseline

- .NET 10 and C#
- WinUI 3 via Windows App SDK 2.3.1
- Windows 10 version 2004 (build 19041) or later; Windows 11 recommended
- MSTest 4
- Visual Studio 2026 or the .NET 10 SDK

## Repository layout

```text
src/
  SeanShell.App/              WinUI 3 host and composition root
  SeanShell.Core/             State, commands, and platform-neutral models
  SeanShell.Windows/          Windows process and shell integration
  SeanShell.Gaming/           Game detection and gaming-mode policy
  SeanShell.PluginContracts/  Stable public plugin boundary
  SeanShell.Plugins/          Manifest validation, lifecycle, and diagnostics host
plugins/
  SeanShell.Plugin.DeveloperTools/  First built-in launcher plugin
  SeanShell.Plugin.Docker/          Cached Docker container launcher plugin
  SeanShell.Plugin.Git/             Cached Git repository launcher plugin
  SeanShell.Plugin.Wsl/             Cached WSL distribution launcher plugin
tests/                        Automated tests
docs/                         Architecture and contributor documentation
tools/                        Recovery and development utilities
assets/                       Shared design and documentation assets
```

## Build

Prerequisites are the .NET 10 SDK, Windows 10 build 19041 or later, and Windows
Developer Mode. Visual Studio users should install the Windows application
development workload.

```powershell
dotnet restore SeanShell.sln
dotnet build SeanShell.sln -c Debug
dotnet test SeanShell.sln -c Debug --no-build
dotnet run --project src/SeanShell.App/SeanShell.App.csproj
```

The packaged WinUI application uses the generated debug identity when launched
from the command line. Its manifest enables Windows package-content integrity
enforcement. A production certificate and release-signing pipeline will be added
after the MVP interaction model is stable.

## Safety boundary

The default remains **Overlay mode** beside Explorer. Users can opt into
**Companion Taskbar mode** from Shell preferences. SeanShell starts a recovery
guard before hiding any primary or secondary Windows taskbar and restores all
taskbars when the option is disabled or SeanShell closes. If the main process is
forced to exit, the independent guard restores them. If the UI and guard both
become unavailable, run `tools/restore-explorer.ps1` from PowerShell.

Gaming mode pauses optional background work; it does not change process priority,
disable Windows security, or inject an overlay into games.

Automatic startup is not enabled yet. The reserved `--startup` launch mode is
protected by a persistent crash-loop guard: three consecutive launches that do
not survive the 30-second startup window disable only automatic launch. A normal
manual launch remains available and clears the guard after it becomes healthy.
The recovery script also starts Explorer when needed and resets this health
history. It first requests a normal SeanShell close, then stops only the
`SeanShell.App` process if WinUI does not close within two seconds.

## Launcher preview

Press `Alt+Space` or use **Open Launcher** on the dashboard. The M1 launcher
indexes Start Menu shortcuts once, combines them with safe built-in Windows
commands, and ranks up to eight results as you type. Use Up/Down to navigate,
Enter to open, and Escape to close.

The shortcut uses the supported `RegisterHotKey` API. It does not install a
keyboard hook or intercept input sent to games. The dashboard can switch between
`Alt+Space`, `Ctrl+Alt+Space`, and `Ctrl+Shift+Space`. If another application owns
the requested shortcut, SeanShell restores the previous shortcut and explains the
conflict; the dashboard button always remains available.

Press `Ctrl+Alt+D` to expand the Dock on the active application's display and
place keyboard focus on its Launcher button. If no active display can be resolved,
SeanShell falls back to the primary display. From there, Tab and standard WinUI navigation
keys reach pinned applications, running windows, Show desktop, and system-area
access without a mouse. Press Escape to return focus to the window that was active
before the Dock opened and immediately collapse an auto-hidden Dock.
`Ctrl+Shift+D` is available as an alternate preset. Dock and Launcher shortcuts
are registered independently, and either one rolls back to its previous binding
if Windows reports a conflict.

The Launcher card measures the first successful window presentation and the most
recent 50 successful provider/ranking operations. It shows the latest search and
nearest-rank P95 beside the M1 targets. Measurements remain in memory for the
current session; query text is never retained or written to disk.

## Dock and live dashboard preview

The M2 preview adds a compact always-on-top dock on each connected display. Each
dock lists ordinary visible application windows on that display as native,
icon-first taskbar items. Pinned and running icons float on one translucent
application capsule instead of separate dark tiles. A short two-tone accent rail
gives the expanded and collapsed Dock a recognizable SeanShell silhouette. The
foreground app alone receives an accent backing and wide underline; compact
neutral markers distinguish background and minimized windows. When keyboard
navigation temporarily makes the Dock itself the Windows foreground surface, the
previous application keeps its active presentation until focus returns.
Full titles, process names, and states remain available to accessibility tools
and pointer tooltips. Multiple top-level windows owned by the same process share
one Dock icon with a numeric badge. Selecting that group opens a native,
keyboard-navigable window picker; arrow keys and Enter choose a window, and
Escape dismisses it. Opening a group's context menu exposes one native submenu
per window with Activate, Minimize or Restore, and graceful Close window actions.
Clicking a single-window item directly toggles it: the active window minimizes,
while another or minimized window restores and activates. A window's context menu
offers Minimize or Restore plus a standard close request, allowing the target
application to show its own unsaved-work prompt. When an explicit Start Menu
identity is available, it also offers Open new instance; ambiguous shortcuts
remain separate choices in a native submenu. Running windows and pinned Start
Menu applications use their Windows-provided icons in a stable 28-pixel slot;
SeanShell falls back to a Fluent glyph when Windows cannot supply one. Pinned
items, running items, and the system area share a compact 48-pixel visual rhythm.
The floating surface uses the active Windows BaseAlt Mica and semantic Layer
brushes. Pointer hover lifts and scales an icon slightly, while press feedback
contracts it; both transitions are removed when Windows requests reduced effects
or Gaming Mode is active.
The floating surface grows with its bounded item count and contracts to a compact
minimum rather than retaining unused horizontal space. In Overlay mode it sits
above the Windows taskbar. In the
opt-in Companion Taskbar mode, it hides every Windows taskbar and becomes the
visible window-switching surface. The dashboard samples CPU, physical memory,
and the current window count every two seconds. Gaming mode stops this polling
and hides the dock.

Dock auto-hide leaves a visible edge indicator instead of disappearing completely.
Pointer entry or keyboard focus expands it, and a dashboard toggle keeps all docks
expanded for users who do not want auto-hide. SeanShell observes Windows display
topology changes and rebuilds monitor-local Dock windows after a short debounce.
If display monitoring or a rebuild fails, the existing Dock windows remain active
and the dashboard explains that a restart is required.

Dock auto-hide, Companion Taskbar mode, and the selected Launcher and Dock shortcuts
persist in a versioned JSON file under `%LOCALAPPDATA%\SeanShell`. Writes use a
temporary file and last-known-good backup. A damaged settings file falls back to
the backup or safe defaults without preventing SeanShell from starting.

The dock does not retain process handles, inject code, attach input queues, or
bypass Windows foreground restrictions.

Companion Taskbar mode covers running-window switching, a Launcher/Start button,
Show desktop / Restore windows, and up to eight pinned Start Menu applications
synchronized across every Dock.
Search for an installed App in Launcher and use its pin button to add or remove
it. A running app with an explicitly matched Start Menu shortcut can also be
pinned from its Dock context menu; multiple matching shortcuts appear in a
submenu instead of being guessed. Running and standalone pinned items both expose
Unpin from Dock plus keyboard-accessible Move left and Move right commands.
Reordering is persisted and synchronized across every display. Pins reuse the
cached Start Menu index, persist locally, and do not add background filesystem
polling. A temporarily missing shortcut is omitted without deleting the pin,
allowing it to return after the shortcut is restored.
Middle-clicking a standalone pin opens a new instance. Middle-clicking a running
item does the same only when its process has an explicit cached application
match; multiple matches open a native choice menu instead of being guessed.
For desktop `.lnk` shortcuts, SeanShell resolves the target executable once
during indexing. When that process owns a window on a Dock's display, the
standalone pin is suppressed and the live window item carries its running,
active, or minimized state instead of showing a duplicate icon. URL and packaged
shortcuts without a reliable process identity remain separate rather than using
title heuristics that could merge unrelated applications.
Pinned items retain a compact pin badge even after coalescing with a running
window. Active windows add an accent surface to their wide underline, while
tooltips and automation names describe active, running, minimized, and pinned
states without relying on color alone.

Explorer remains running for notifications, system tray services, input methods,
file associations, and recovery. Every Dock now shows the current time and date
using the user's Windows regional formats. While Companion Taskbar mode is active,
its system-area button can reveal every native Windows taskbar for direct
notification and system-tray access without simulating input or enumerating tray
icons. Selecting it again resumes replacement; entering Gaming Mode also
re-hides the native taskbars. The reveal state is session-only and never weakens
crash recovery.

While replacement is active, each Dock registers a bottom-edge Windows AppBar
reservation on its display. Maximized applications therefore stop above the Dock
instead of rendering underneath it. SeanShell releases these per-display
reservations before revealing the native system area, entering Gaming Mode,
disabling replacement, or exiting; Windows also drops them automatically if a
Dock process is terminated.

Production iconography now begins with native application icons while keeping
SeanShell controls in the Segoe Fluent Icons family. Windows ExtraLarge Shell
icons are retained as 48×48 BGRA sources and rendered into the stable 26px slot,
improving high-DPI downsampling without changing Dock geometry. Additional
effects remain subordinate to replacement reliability and accessibility. The
SeanShell package, title bar, and Dock Launcher now share a reproducibly
generated terminal-prompt brand mark that stays legible from 16 to 256 pixels
and across light, dark, and transparent Windows surfaces.

## Gaming mode preview

The M3 preview supports a manual override and opt-in automatic detection. Add one
game process name per line in the dashboard, enable automatic detection, and
SeanShell checks a disposable process snapshot every two seconds. Matching is
case-insensitive and accepts names with or without `.exe`. Only matching
processes become snapshots, so SeanShell does not allocate or sort dashboard data
for every running process.

While gaming mode is active, dashboard sampling stops and every Dock window is
hidden. The small process detector remains active so SeanShell can restore the
workspace after the last matching game exits. Steam and other launchers are not
matched unless the user explicitly adds them. No process handles are retained.
The Gaming mode card keeps a bounded 60-sample diagnostic window with the latest
scan time, scan P95, and estimated detector CPU percentage.

Detected sessions are recorded locally after the last matching process exits.
SeanShell retains the 20 most recent summaries in
`LocalApplicationData/SeanShell/gaming-sessions.json`, including executable names,
start/end times, Windows and SeanShell versions, and detector metrics. Session
history is never uploaded.

## Plugin platform preview

The M4 preview adds a bounded host for explicitly registered built-in plugins.
Every plugin declares a versioned manifest, minimum host API, publisher, and
capabilities. Initialization and lifecycle calls have time limits; launcher
queries are limited to 250 ms. A plugin that throws or exceeds a limit is marked
faulted and removed from subsequent queries without affecting healthy providers.

Expand **Plugins** on the dashboard to inspect state, capabilities, last operation,
duration, and recoverable errors. Gaming mode suspends active plugins and resumes
them when normal mode returns. The included Developer tools plugin contributes
Windows Developer Settings and Environment Variables to Launcher search.

Each built-in plugin has an **Enabled** switch. Disabling a plugin suspends it,
removes its Launcher commands, and persists the choice across restarts. A plugin
disabled at startup is not initialized until it is enabled. If saving fails,
SeanShell restores the previous runtime state and reports the failure.

The built-in **Git repositories** plugin first resolves the repository containing
the current working directory or application build output, then scans common
`GitHub`, `Repos`, `Repositories`, and Visual Studio `source/repos` locations to a
depth of two folders. It caches up to twelve repositories and adds
Launcher results for opening the folder, VS Code, or Windows Terminal. Branch,
working-tree change count, and ahead/behind state are read with `git status`.
The plugin never runs commands that modify a repository. Use **Refresh Git
repositories** in Launcher after creating, moving, or changing a repository.

The built-in **WSL distributions** plugin reads `wsl.exe --list --verbose` once
during initialization and again only when the user runs **Refresh WSL
distributions**. It adds Launcher results for opening each user distribution or
browsing its `\\wsl.localhost` files. Docker Desktop's internal distributions are
excluded and remain owned by Docker Desktop. A user-selected open action may start
a stopped distribution; SeanShell never terminates, unregisters, imports, exports,
or changes the default distribution. Plugin results use explicit names such as
**Ubuntu WSL shell** and **Ubuntu WSL files** so they remain distinguishable from
the ordinary Ubuntu Start Menu application.

The built-in **Docker containers** plugin reads `docker container ls --all` once
during initialization and again only when the user runs **Refresh Docker
containers**. Docker Desktop and the Docker Engine are not started automatically.
An offline Engine is shown as an available refresh action rather than faulting the
plugin. Cached containers contribute commands to follow the last 200 log lines;
running containers also expose published TCP ports on `localhost`. The plugin
never starts, stops, restarts, executes inside, removes, pulls, or changes a
container.

The built-in **.NET workspaces** plugin scans the same bounded developer roots for
up to 24 `.sln`, `.slnx`, and `.csproj` files. It safely reads project SDK and
target-framework metadata and distinguishes C#, ASP.NET Core, Blazor WebAssembly,
Razor-component web apps, .NET Worker, and .NET MAUI projects. Cached Launcher
commands open a workspace in its default IDE, VS Code, or Windows Terminal.
Use **Refresh .NET workspaces** after creating or moving a project. Explicit
Launcher actions can build any cached workspace, test solutions and recognized
test projects, or run recognized executable and web projects. Each result shows
the exact `dotnet` command before selection and passes arguments directly to a new
Windows Terminal session without `cmd.exe` or PowerShell. Runnable projects also
receive a `dotnet watch run` hot-reload action. Local HTTP/HTTPS endpoints from
`Properties/launchSettings.json` can be opened directly; external and non-web
URLs are ignored.

SeanShell also performs a diagnostic-only scan of up to 32 immediate package
directories under `%LOCALAPPDATA%\SeanShell\plugins`. It validates a bounded
manifest, package-contained non-linked DLL path, content hash, Windows
Authenticode trust, and the declared publisher certificate fingerprint. Passing
packages can receive explicit local consent for their exact signer fingerprint
and requested capabilities. A changed signer or expanded capability set requires
new consent, and the dashboard can revoke a candidate or clear every stored
decision even after a package is removed. No external DLL is loaded or executed.

The broker runtime now contains an inactive, collectible load context that
accepts only exact-hash manifest dependencies, trusted framework assemblies,
and explicitly supplied shared contracts. Undeclared managed and native names
fail closed, and managed DLLs load from the same verified open stream rather
than reopening an unlocked path. It is deliberately not connected to a broker
operation.
The runtime also defines strict, bounded command query, descriptor, invocation,
and result DTOs with no delegate, executable path, argument list, URL, or shell
field. Command invocation is an opaque ID bound to the descriptor-set SHA-256.
These DTOs are not part of any enabled broker operation. Native-load staging,
activation lifecycle/deadlines, and production release signing must still ship
before external plugins can be accepted for execution. See the
[plugin specification](docs/plugin-spec.md) for the preview manifest and consent
boundary.

Protocol v4 now runs in a fail-closed child process of the exact packaged
`SeanShell.App.exe`. A custom entry point selects broker mode before WinUI or
WinRT initialization; normal launches continue into the desktop UI. The
standalone `SeanShell.PluginBroker` executable remains only as a test harness,
and both hosts share `SeanShell.PluginBroker.Runtime`. The protocol accepts
bounded `health` and read-only
`probe-metadata` requests over a per-process HMAC-authenticated channel; the host
verifies the response envelope, PID, and request ID within two seconds. Every
broker receives a one-process, 256 MiB, kill-on-close Windows Job Object before
its suspended primary thread is resumed. Only the three redirected streams and
a one-use session-key pipe are inherited. The broker then disables
legacy extension points, unsafe image sources, and child-process creation before
reading a request. The protocol still never loads or activates a plugin, and the
App does not use the new load context to run external code. See the
[broker protocol](docs/plugin-broker-protocol.md).

An external manifest may declare up to 32 managed or native package DLLs.
The host checks their canonical package paths, individual and aggregate sizes,
SHA-256 identities, Authenticode trust, and exact publisher certificate. The
broker independently repeats path, reparse-point, size, and hash checks and
returns only a dependency-set digest. No dependency is loaded.

External metadata probes also keep a separate, atomic broker-health history.
Three broker timeouts, malformed/authentication failures, or truncated responses
for the same plugin inside ten minutes quarantine that plugin for thirty minutes.
A successful probe clears its failure window. User cancellation, trust-scan
failure, and a missing broker installation do not count against a plugin.

## Documentation

- [Architecture](docs/architecture.md)
- [Design system](docs/design-system.md)
- [Command flow](docs/command-flow.md)
- [Data flow](docs/data-flow.md)
- [Gaming compatibility](docs/gaming-compatibility.md)
- [Plugin specification](docs/plugin-spec.md)
- [Plugin broker protocol](docs/plugin-broker-protocol.md)
- [Roadmap](docs/roadmap.md)
- [Contributing](docs/contributing.md)

## License

SeanShell is licensed under the [MIT License](LICENSE).
