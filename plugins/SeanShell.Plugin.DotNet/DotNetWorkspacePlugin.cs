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
    private readonly IReadOnlyList<string> _roots;
    private IReadOnlyList<DotNetWorkspaceSnapshot> _workspaces = [];
    private bool _disposed;

    public DotNetWorkspacePlugin(IEnumerable<string> workspaceRoots)
    {
        ArgumentNullException.ThrowIfNull(workspaceRoots);
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
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        var commands = new List<ShellCommand>
        {
            new(
                $"plugin:{Manifest.Id}:refresh",
                "Refresh .NET workspaces",
                $"{_workspaces.Count} cached workspace{(_workspaces.Count == 1 ? string.Empty : "s")}",
                RefreshAsync)
            {
                Kind = ShellCommandKind.Plugin,
                Glyph = "\uE72C",
                Keywords = ["dotnet", ".net", "csharp", "blazor", "solution", "project", "rescan"],
            },
        };

        foreach (var workspace in _workspaces)
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
        _disposed = true;
        return ValueTask.CompletedTask;
    }

    private async ValueTask RefreshAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _workspaces = DotNetWorkspaceDiscovery.Discover(
                    _roots,
                    cancellationToken: cancellationToken)
                .Select(DotNetWorkspaceInspector.Inspect)
                .Where(static workspace => workspace is not null)
                .Cast<DotNetWorkspaceSnapshot>()
                .OrderByDescending(static workspace => workspace.IsSolution)
                .ThenBy(static workspace => workspace.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static workspace => workspace.Path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        finally
        {
            _refreshGate.Release();
        }
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

    private static string CreateStableId(string path)
    {
        var bytes = SHA256.HashData(
            Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToUpperInvariant()));
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
}
