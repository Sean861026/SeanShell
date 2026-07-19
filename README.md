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
plugins/                      Built-in and sample plugins (future milestones)
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

## Launcher preview

Press `Alt+Space` or use **Open Launcher** on the dashboard. The M1 launcher
indexes Start Menu shortcuts once, combines them with safe built-in Windows
commands, and ranks up to eight results as you type. Use Up/Down to navigate,
Enter to open, and Escape to close.

The shortcut uses the supported `RegisterHotKey` API. It does not install a
keyboard hook or intercept input sent to games. If another application already
owns `Alt+Space`, the dashboard button remains available and explains the conflict.

## Dock and live dashboard preview

The M2 preview adds a compact always-on-top dock above each connected display's
taskbar. Each dock lists ordinary visible application windows on that display and
switches to a selected window. The
dashboard samples CPU, physical memory, and the current window count every two
seconds. Gaming mode stops this polling and hides the dock.

Dock auto-hide leaves a visible edge indicator instead of disappearing completely.
Pointer entry or keyboard focus expands it, and a dashboard toggle keeps all docks
expanded for users who do not want auto-hide. Display topology is captured at
startup; restart SeanShell after connecting or disconnecting a monitor.

The dock does not retain process handles, inject code, attach input queues, or
bypass Windows foreground restrictions.

## Documentation

- [Architecture](docs/architecture.md)
- [Command flow](docs/command-flow.md)
- [Data flow](docs/data-flow.md)
- [Plugin specification](docs/plugin-spec.md)
- [Roadmap](docs/roadmap.md)
- [Contributing](docs/contributing.md)

## License

SeanShell is licensed under the [MIT License](LICENSE).
