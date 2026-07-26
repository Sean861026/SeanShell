# SeanShell

A modular Windows developer shell focused on performance, productivity, and gaming compatibility.

SeanShell starts as a safe companion to Windows Explorer. It does **not** replace
`explorer.exe`, modify Winlogon, inject into other processes, or hook graphics and
input APIs. The first milestone validates a launcher, developer dashboard, dock,
plugin boundary, and gaming mode while preserving a direct path back to the
standard Windows desktop.

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
from the command line. Packaging and signing for distribution will be added after
the MVP interaction model is stable.

## Safety boundary

The MVP runs in **Overlay mode** beside Explorer. Gaming mode pauses optional
background work; it does not change process priority, disable Windows security,
or inject an overlay into games. If the UI becomes unavailable, run
`tools/restore-explorer.ps1` from PowerShell.

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

The Launcher card measures the first successful window presentation and the most
recent 50 successful provider/ranking operations. It shows the latest search and
nearest-rank P95 beside the M1 targets. Measurements remain in memory for the
current session; query text is never retained or written to disk.

## Dock and live dashboard preview

The M2 preview adds a compact always-on-top dock above each connected display's
taskbar. Each dock lists ordinary visible application windows on that display and
switches to a selected window. The
dashboard samples CPU, physical memory, and the current window count every two
seconds. Gaming mode stops this polling and hides the dock.

Dock auto-hide leaves a visible edge indicator instead of disappearing completely.
Pointer entry or keyboard focus expands it, and a dashboard toggle keeps all docks
expanded for users who do not want auto-hide. SeanShell observes Windows display
topology changes and rebuilds monitor-local Dock windows after a short debounce.
If display monitoring or a rebuild fails, the existing Dock windows remain active
and the dashboard explains that a restart is required.

Dock auto-hide and the selected Launcher shortcut persist in a versioned JSON file
under `%LOCALAPPDATA%\SeanShell`. Writes use a temporary file and last-known-good
backup. A damaged settings file falls back to the backup or safe defaults without
preventing SeanShell from starting.

The dock does not retain process handles, inject code, attach input queues, or
bypass Windows foreground restrictions.

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

The first M4 slice adds a bounded host for explicitly registered built-in plugins.
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

Arbitrary third-party DLL discovery is intentionally disabled. Signing, consent,
and stronger process isolation must ship before external plugins are accepted.

## Documentation

- [Architecture](docs/architecture.md)
- [Design system](docs/design-system.md)
- [Command flow](docs/command-flow.md)
- [Data flow](docs/data-flow.md)
- [Gaming compatibility](docs/gaming-compatibility.md)
- [Plugin specification](docs/plugin-spec.md)
- [Roadmap](docs/roadmap.md)
- [Contributing](docs/contributing.md)

## License

SeanShell is licensed under the [MIT License](LICENSE).
