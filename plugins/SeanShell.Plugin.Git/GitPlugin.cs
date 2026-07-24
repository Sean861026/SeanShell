using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using SeanShell.Core;
using SeanShell.PluginContracts;

namespace SeanShell.Plugin.Git;

public sealed class GitPlugin : ISeanShellPlugin
{
    public static PluginManifest Manifest { get; } = new(
        PluginManifest.CurrentSchemaVersion,
        "seanshell.git",
        "Git repositories",
        "0.1.0",
        1,
        "SeanShell",
        PluginCapability.LauncherCommands,
        true);

    private readonly IGitRepositoryInspector _inspector;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly IReadOnlyList<string> _roots;
    private IReadOnlyList<GitRepositorySnapshot> _repositories = [];
    private bool _disposed;

    public GitPlugin(
        IEnumerable<string> repositoryRoots,
        IGitRepositoryInspector? inspector = null)
    {
        ArgumentNullException.ThrowIfNull(repositoryRoots);
        _roots = repositoryRoots
            .Where(static root => !string.IsNullOrWhiteSpace(root))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _inspector = inspector ?? new GitProcessRepositoryInspector();
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
                "Refresh Git repositories",
                $"Rescan {_roots.Count} configured location{(_roots.Count == 1 ? string.Empty : "s")}",
                RefreshAsync)
            {
                Kind = ShellCommandKind.Plugin,
                Glyph = "\uE72C",
                Keywords = ["git", "repository", "rescan", "reload"],
            },
        };

        foreach (var repository in _repositories)
        {
            var stableId = CreateStableId(repository.Path);
            commands.Add(CreateCommand(
                $"{stableId}:open",
                repository.Name,
                $"{repository.StatusText} \u00B7 Open repository folder",
                ["git", "repository", repository.Branch],
                _ => LaunchDirectoryAsync(repository.Path)));
            commands.Add(CreateCommand(
                $"{stableId}:code",
                $"{repository.Name} in VS Code",
                repository.StatusText,
                ["git", "repository", "code", "vscode", repository.Branch],
                _ => LaunchAsync("code", repository.Path)));
            commands.Add(CreateCommand(
                $"{stableId}:terminal",
                $"{repository.Name} terminal",
                $"{repository.StatusText} \u00B7 Open Windows Terminal",
                ["git", "repository", "terminal", repository.Branch],
                _ => LaunchAsync("wt.exe", "-d", repository.Path)));
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
            var paths = GitRepositoryDiscovery.Discover(
                _roots,
                cancellationToken: cancellationToken);
            var inspections = paths
                .Select(path => _inspector.InspectAsync(path, cancellationToken).AsTask())
                .ToArray();
            var snapshots = await Task.WhenAll(inspections).ConfigureAwait(false);
            _repositories = snapshots
                .Where(static snapshot => snapshot is not null)
                .Cast<GitRepositorySnapshot>()
                .OrderBy(static repository => repository.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static repository => repository.Path, StringComparer.OrdinalIgnoreCase)
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
            Glyph = "\uE8F1",
            Keywords = keywords,
        };

    private static string CreateStableId(string path)
    {
        var bytes = SHA256.HashData(
            Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToUpperInvariant()));
        return Convert.ToHexString(bytes.AsSpan(0, 8)).ToLowerInvariant();
    }

    private static ValueTask LaunchDirectoryAsync(string path)
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
