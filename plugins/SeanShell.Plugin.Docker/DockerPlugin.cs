using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using SeanShell.Core;
using SeanShell.PluginContracts;

namespace SeanShell.Plugin.Docker;

public sealed class DockerPlugin : ISeanShellPlugin
{
    public static PluginManifest Manifest { get; } = new(
        PluginManifest.CurrentSchemaVersion,
        "seanshell.docker",
        "Docker containers",
        "0.1.0",
        1,
        "SeanShell",
        PluginCapability.LauncherCommands,
        true);

    private readonly IDockerContainerProvider _provider;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private DockerContainerQueryResult _snapshot =
        DockerContainerQueryResult.EngineUnavailable;
    private bool _disposed;

    public DockerPlugin(IDockerContainerProvider? provider = null)
    {
        _provider = provider ?? new DockerProcessContainerProvider();
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
                "Refresh Docker containers",
                _snapshot.StatusText,
                RefreshAsync)
            {
                Kind = ShellCommandKind.Plugin,
                Glyph = "\uE72C",
                Keywords = ["docker", "container", "rescan", "reload", "engine"],
            },
        };

        if (!_snapshot.IsAvailable)
        {
            return ValueTask.FromResult<IReadOnlyList<ShellCommand>>(commands);
        }

        foreach (var container in _snapshot.Containers)
        {
            var stableId = CreateStableId(container.Id);
            commands.Add(CreateCommand(
                $"{stableId}:logs",
                $"{container.Name} Docker logs",
                $"{container.StatusText} · Show last 200 lines and follow",
                ["docker", "container", "logs", container.Image, container.State],
                _ => OpenLogsAsync(container.Id)));

            if (!string.Equals(
                    container.State,
                    "running",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var port in container.PublishedPorts)
            {
                var url = $"http://localhost:{port.HostPort}";
                commands.Add(CreateCommand(
                    $"{stableId}:port:{port.HostPort}:{port.ContainerPort}",
                    $"{container.Name} Docker port {port.HostPort}",
                    $"{container.StatusText} · Open {url}",
                    [
                        "docker",
                        "container",
                        "port",
                        "localhost",
                        container.Image,
                        port.ContainerPort.ToString(),
                    ],
                    _ => OpenUrlAsync(url)));
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
        _disposed = true;
        return ValueTask.CompletedTask;
    }

    private async ValueTask RefreshAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _snapshot = await _provider
                .GetContainersAsync(cancellationToken)
                .ConfigureAwait(false);
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
            Glyph = "\uE7B8",
            Keywords = keywords,
        };

    private static string CreateStableId(string id)
    {
        var bytes = SHA256.HashData(
            Encoding.UTF8.GetBytes(id.ToUpperInvariant()));
        return Convert.ToHexString(bytes.AsSpan(0, 8)).ToLowerInvariant();
    }

    private static ValueTask OpenLogsAsync(string containerId)
    {
        Process.Start(DockerCommandStartInfoFactory.CreateLogs(containerId));
        return ValueTask.CompletedTask;
    }

    private static ValueTask OpenUrlAsync(string url)
    {
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        return ValueTask.CompletedTask;
    }
}
