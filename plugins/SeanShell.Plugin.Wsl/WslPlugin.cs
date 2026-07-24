using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using SeanShell.Core;
using SeanShell.PluginContracts;

namespace SeanShell.Plugin.Wsl;

public sealed class WslPlugin : ISeanShellPlugin
{
    public static PluginManifest Manifest { get; } = new(
        PluginManifest.CurrentSchemaVersion,
        "seanshell.wsl",
        "WSL distributions",
        "0.1.0",
        1,
        "SeanShell",
        PluginCapability.LauncherCommands,
        true);

    private readonly IWslDistributionProvider _provider;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private IReadOnlyList<WslDistributionSnapshot> _distributions = [];
    private bool _disposed;

    public WslPlugin(IWslDistributionProvider? provider = null)
    {
        _provider = provider ?? new WslProcessDistributionProvider();
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
                "Refresh WSL distributions",
                $"{_distributions.Count} installed distribution{(_distributions.Count == 1 ? string.Empty : "s")}",
                RefreshAsync)
            {
                Kind = ShellCommandKind.Plugin,
                Glyph = "\uE72C",
                Keywords = ["wsl", "linux", "distribution", "rescan", "reload"],
            },
        };

        foreach (var distribution in _distributions)
        {
            var stableId = CreateStableId(distribution.Name);
            commands.Add(CreateCommand(
                $"{stableId}:shell",
                $"{distribution.Name} WSL shell",
                $"{distribution.StatusText} \u00B7 Open Linux shell",
                ["wsl", "linux", "terminal", distribution.State],
                _ => LaunchWslAsync(distribution.Name)));
            commands.Add(CreateCommand(
                $"{stableId}:files",
                $"{distribution.Name} WSL files",
                $"{distribution.StatusText} \u00B7 Open Linux files",
                ["wsl", "linux", "files", "explorer", distribution.State],
                _ => OpenFilesAsync(distribution.Name)));
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
            var distributions = await _provider
                .GetDistributionsAsync(cancellationToken)
                .ConfigureAwait(false);
            _distributions = distributions
                .Where(static distribution =>
                    !distribution.Name.StartsWith(
                        "docker-desktop",
                        StringComparison.OrdinalIgnoreCase))
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
            Glyph = "\uE756",
            Keywords = keywords,
        };

    private static string CreateStableId(string name)
    {
        var bytes = SHA256.HashData(
            Encoding.UTF8.GetBytes(name.ToUpperInvariant()));
        return Convert.ToHexString(bytes.AsSpan(0, 8)).ToLowerInvariant();
    }

    private static ValueTask LaunchWslAsync(string distributionName)
    {
        Process.Start(WslShellStartInfoFactory.Create(distributionName));
        return ValueTask.CompletedTask;
    }

    private static ValueTask OpenFilesAsync(string distributionName)
    {
        var path = $@"\\wsl.localhost\{distributionName}";
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        return ValueTask.CompletedTask;
    }
}
