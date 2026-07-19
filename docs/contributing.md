# Contributing

## Development setup

Use Windows 10 build 19041 or later with the .NET 10 SDK. Windows 11 and Visual
Studio 2026 with the Windows application development workload are recommended.
Enable Developer Mode before running the packaged WinUI app.

```powershell
dotnet restore SeanShell.sln
dotnet build SeanShell.sln -c Debug
dotnet test SeanShell.sln -c Debug --no-build
```

## Change guidelines

1. Create a focused branch and explain the user impact in the pull request.
2. Keep platform calls in `SeanShell.Windows`; keep Core free of WinUI and Win32.
3. Add tests for state transitions, ranking, configuration, and gaming rules.
4. Update command-flow and data-flow documents when behavior crosses components.
5. Measure idle CPU, memory, startup time, and gaming-mode behavior for shell work.

## Safety requirements

Changes that inject into other processes, install kernel drivers, hook graphics or
global input, weaken Windows security, or remove recovery paths are out of scope.
Do not add code that changes the configured Windows shell until a reviewed recovery
design and crash-loop guard exist.

Report security-sensitive findings privately to the repository owner rather than
including exploit details in a public issue.
