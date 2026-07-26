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

Pull-request CI runs on Windows with .NET 10 and x64 Release settings. It verifies
formatting, parses the Explorer recovery script without executing it, builds the
full WinUI solution, and runs the automated tests. New changes should pass the
equivalent local checks before being pushed:

```powershell
dotnet format SeanShell.sln --no-restore --verify-no-changes --verbosity minimal
dotnet build SeanShell.sln -c Release -p:Platform=x64 --no-restore
dotnet test SeanShell.sln -c Release -p:Platform=x64 --no-build
```

## Change guidelines

1. Create a focused branch and explain the user impact in the pull request.
2. Keep platform calls in `SeanShell.Windows`; keep Core free of WinUI and Win32.
3. Add tests for state transitions, ranking, configuration, and gaming rules.
4. Update command-flow and data-flow documents when behavior crosses components.
5. Measure idle CPU, memory, startup time, and gaming-mode behavior for shell work.
6. Resolve pinned applications only from the bounded installed-application cache;
   do not add Dock-time filesystem scanning or execute an unindexed settings path.

## Safety requirements

Changes that inject into other processes, install kernel drivers, hook graphics or
global input, weaken Windows security, or remove recovery paths are out of scope.
Companion Taskbar changes must keep Explorer running, start an independent
recovery guard before hiding any taskbar, and verify graceful and forced-exit
restoration on every connected display. Do not change the configured Winlogon
shell until a separate reviewed recovery design and compatibility gate exist.

External package contributions must remain data-only until the brokered loading
milestone. Do not add reflection, `Assembly.Load`, dependency resolution, or type
activation to the candidate catalog. Path containment and Authenticode checks are
diagnostic evidence, not permission to execute a candidate in the SeanShell
process. Consent changes must fail closed, bind the exact signer and capabilities,
remain revocable without the package being present, and update memory only after
the atomic trust-document write succeeds.

Broker protocol changes require cross-process tests for frame bounds, version and
request/session correlation, authentication tampering and replay, rejected
operations, exit behavior, timeout cleanup, process identity, persisted failure
windows, recovery, and quarantine expiry. User cancellation and host
installation failures must never be charged to a plugin. Do not add an
activation operation until its dependency-containment, bounded DTO, and
packaging design is reviewed.

Resolver changes must prove that undeclared managed dependencies cannot fall
back to assemblies already present in the host, undeclared native names are
rejected, changed hashes fail at resolution time, shared contracts cannot be
shadowed, and dependency count/path/size limits are repeated at the load
boundary. The resolver must remain disconnected from protocol operations until
the bounded activation DTO and native staging designs are reviewed.

Broker command DTOs must stay data-only and bounded. Do not add delegates,
reflection objects, executable paths, process arguments, URLs, local paths, or
shell strings. New fields require strict-codec unknown-member tests, individual
and aggregate bounds, canonical digest coverage where applicable, and an
explicit capability review. The reserved DTOs must remain disconnected from
protocol v4 until the activation lifecycle is reviewed.

CI sets `SEANSHELL_BROKER_TEST_EXECUTABLE` to the freshly built
`SeanShell.App.exe`, forcing all process-boundary tests through the same custom
broker entry point used by packaged production composition. Local tests fall
back to the standalone console harness. The package manifest must retain
`uap10:PackageIntegrity` with content enforcement enabled.

Report security-sensitive findings privately to the repository owner rather than
including exploit details in a public issue.
