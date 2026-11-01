using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using SeanShell.Core;
using SeanShell.PluginContracts;

namespace SeanShell.Plugin.DotNet;

public sealed class DotNetWorkspacePlugin : ISeanShellPlugin
{
    public static PluginManifest Manifest { get; } = new(
        PluginManifest.CurrentSchemaVersion,
        "seanshell.dotnet",
        ".NET workspaces",
        "0.1.0",
        1,
        "SeanShell",
        PluginCapability.LauncherCommands,
        true);

    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly object _stateLock = new();
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly IReadOnlyList<string> _roots;
    private readonly Func<IReadOnlyList<string>, CancellationToken, IReadOnlyList<DotNetWorkspaceSnapshot>> _scan;
    private IReadOnlyList<DotNetWorkspaceSnapshot> _workspaces = [];
    private Task? _disposal;
    private bool _disposed;

    public DotNetWorkspacePlugin(IEnumerable<string> workspaceRoots)
        : this(workspaceRoots, ScanWorkspaces)
    {
    }

    internal DotNetWorkspacePlugin(
        IEnumerable<string> workspaceRoots,
        Func<IReadOnlyList<string>, CancellationToken, IReadOnlyList<DotNetWorkspaceSnapshot>> scan)
    {
        ArgumentNullException.ThrowIfNull(workspaceRoots);
        ArgumentNullException.ThrowIfNull(scan);
        _scan = scan;
        _roots = workspaceRoots
            .Where(static root => !string.IsNullOrWhiteSpace(root))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public string Id => Manifest.Id;

    public string Name => Manifest.Name;

    public async ValueTask InitializeAsync(CancellationToken cancellationToken) =>
        await RefreshAsync(cancellationToken).ConfigureAwait(false);

    public ValueTask<IReadOnlyList<ShellCommand>> GetCommandsAsync(
        string query,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<DotNetWorkspaceSnapshot> workspaces;
        lock (_stateLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            workspaces = _workspaces;
        }

        var commands = new List<ShellCommand>
        {
            new(
                $"plugin:{Manifest.Id}:refresh",
                "Refresh .NET workspaces",
                $"{workspaces.Count} cached workspace{(workspaces.Count == 1 ? string.Empty : "s")}",
                RefreshAsync)
            {
                ExecutionTimeout = TimeSpan.FromSeconds(30),
                Kind = ShellCommandKind.Plugin,
                Glyph = "\uE72C",
                Keywords = ["dotnet", ".net", "csharp", "blazor", "solution", "project", "rescan"],
            },
        };

        foreach (var workspace in workspaces)
        {
            var stableId = CreateStableId(workspace.Path);
            var displayName = workspace.IsSolution
                ? $"{workspace.Name} solution"
                : workspace.Name;
            var keywords = new[]
                {
                    "dotnet",
                    ".net",
                    "csharp",
                    "solution",
                    "project",
                    workspace.ProjectType,
                }
                .Concat(workspace.TargetFrameworks)
                .ToArray();

            commands.Add(CreateCommand(
                $"{stableId}:open",
                displayName,
                $"{workspace.StatusText} \u00B7 Open in default IDE",
                keywords,
                _ => OpenAsync(workspace.Path)));
            commands.Add(CreateCommand(
                $"{stableId}:code",
                $"{displayName} in VS Code",
                workspace.StatusText,
                keywords.Concat(["code", "vscode"]).ToArray(),
                _ => LaunchAsync("code", "-r", workspace.DirectoryPath)));
            commands.Add(CreateCommand(
                $"{stableId}:terminal",
                $"{displayName} terminal",
                $"{workspace.StatusText} \u00B7 Open Windows Terminal",
                keywords.Concat(["terminal"]).ToArray(),
                _ => LaunchAsync("wt.exe", "-d", workspace.DirectoryPath)));
            commands.Add(CreateDotNetCommand(
                $"{stableId}:build",
                $"Build {displayName}",
                workspace,
                keywords.Concat(["build"]).ToArray(),
                "build",
                workspace.Path));
            if (workspace.IsSolution || workspace.IsTestProject)
            {
                commands.Add(CreateDotNetCommand(
                    $"{stableId}:test",
                    $"Test {displayName}",
                    workspace,
                    keywords.Concat(["test"]).ToArray(),
                    "test",
                    workspace.Path));
            }

            if (workspace.IsRunnable)
            {
                commands.Add(CreateDotNetCommand(
                    $"{stableId}:run",
                    $"Run {displayName}",
                    workspace,
                    keywords.Concat(["run"]).ToArray(),
                    "run",
                    "--project",
                    workspace.Path));
                commands.Add(CreateDotNetCommand(
                    $"{stableId}:watch",
                    $"Watch {displayName}",
                    workspace,
                    keywords.Concat(["watch", "hot reload"]).ToArray(),
                    "watch",
                    "run",
                    "--project",
                    workspace.Path));
            }

            foreach (var url in workspace.LocalApplicationUrls)
            {
                commands.Add(CreateCommand(
                    $"{stableId}:url:{CreateValueId(url)}",
                    $"Open {workspace.Name} at {url}",
                    "Local launch profile \u00B7 Open in default browser",
                    keywords.Concat(["localhost", "browser", "launch profile", url]).ToArray(),
                    _ => OpenAsync(url)));
            }
        }

        return ValueTask.FromResult<IReadOnlyList<ShellCommand>>(commands);
    }

    public ValueTask SuspendAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }

    public ValueTask ResumeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        lock (_stateLock)
        {
            if (_disposal is not null)
            {
                return new(_disposal);
            }

            _disposed = true;
            _disposal = DisposeCoreAsync();
            return new(_disposal);
        }
    }

    private async Task DisposeCoreAsync()
    {
        await _lifetimeCancellation.CancelAsync().ConfigureAwait(false);
        // Keep the gate alive until the worker actually exits, even if its caller
        // cancels. In-flight filesystem operations cannot always be interrupted.
        await _refreshGate.WaitAsync().ConfigureAwait(false);
        _refreshGate.Release();
        _lifetimeCancellation.Dispose();
    }

    private async ValueTask RefreshAsync(CancellationToken cancellationToken)
    {
        CancellationTokenSource refreshCancellation;
        lock (_stateLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            refreshCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _lifetimeCancellation.Token);
        }

        using var cancellation = refreshCancellation;
        var token = cancellation.Token;
        await _refreshGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            // An uncontended WaitAsync completes synchronously. Explicitly move
            // discovery, XML parsing, and Razor traversal off the calling UI thread.
            var workspaces = await Task.Run(() => _scan(_roots, token), token)
                .ConfigureAwait(false);
            lock (_stateLock)
            {
                token.ThrowIfCancellationRequested();
                ObjectDisposedException.ThrowIf(_disposed, this);
                _workspaces = workspaces;
            }
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private static IReadOnlyList<DotNetWorkspaceSnapshot> ScanWorkspaces(
        IReadOnlyList<string> roots,
        CancellationToken cancellationToken)
    {
        var workspaces = new List<DotNetWorkspaceSnapshot>();
        foreach (var path in DotNetWorkspaceDiscovery.Discover(
            roots,
            cancellationToken: cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (DotNetWorkspaceInspector.Inspect(path, cancellationToken) is { } workspace)
            {
                workspaces.Add(workspace);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return workspaces
            .OrderByDescending(static workspace => workspace.IsSolution)
            .ThenBy(static workspace => workspace.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static workspace => workspace.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static ShellCommand CreateCommand(
        string id,
        string title,
        string subtitle,
        IReadOnlyList<string> keywords,
        Func<CancellationToken, ValueTask> executeAsync) =>
        new($"plugin:{Manifest.Id}:{id}", title, subtitle, executeAsync)
        {
            Kind = ShellCommandKind.Plugin,
            Glyph = "\uE943",
            Keywords = keywords,
        };

    private static ShellCommand CreateDotNetCommand(
        string id,
        string title,
        DotNetWorkspaceSnapshot workspace,
        IReadOnlyList<string> keywords,
        params string[] dotnetArguments) =>
        CreateCommand(
            id,
            title,
            DotNetCommandStartInfoFactory.Format(dotnetArguments),
            keywords,
            _ => LaunchDotNetAsync(workspace.DirectoryPath, dotnetArguments));

    private static string CreateStableId(string path)
    {
        return CreateValueId(Path.GetFullPath(path));
    }

    private static string CreateValueId(string value)
    {
        var bytes = SHA256.HashData(
            Encoding.UTF8.GetBytes(value.ToUpperInvariant()));
        return Convert.ToHexString(bytes.AsSpan(0, 8)).ToLowerInvariant();
    }

    private static ValueTask OpenAsync(string path)
    {
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        return ValueTask.CompletedTask;
    }

    private static ValueTask LaunchAsync(string fileName, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(fileName) { UseShellExecute = true };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        Process.Start(startInfo);
        return ValueTask.CompletedTask;
    }

    private static ValueTask LaunchDotNetAsync(
        string workingDirectory,
        params string[] dotnetArguments)
    {
        Process.Start(DotNetCommandStartInfoFactory.Create(
            workingDirectory,
            dotnetArguments));
        return ValueTask.CompletedTask;
    }
}
