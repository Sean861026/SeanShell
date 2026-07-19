namespace SeanShell.Core;

public interface ILauncherCommandProvider
{
    ValueTask<IReadOnlyList<ShellCommand>> GetCommandsAsync(
        string query,
        CancellationToken cancellationToken);
}
