using SeanShell.Core;

namespace SeanShell.PluginContracts;

public interface ISeanShellPlugin : IAsyncDisposable
{
    string Id { get; }

    string Name { get; }

    ValueTask InitializeAsync(CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<ShellCommand>> GetCommandsAsync(
        string query,
        CancellationToken cancellationToken);

    ValueTask SuspendAsync(CancellationToken cancellationToken);

    ValueTask ResumeAsync(CancellationToken cancellationToken);
}
