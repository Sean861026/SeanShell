namespace SeanShell.Core;

public sealed class LauncherSearchService(IEnumerable<ILauncherCommandProvider> providers)
{
    private readonly ILauncherCommandProvider[] _providers = providers.ToArray();

    public async ValueTask<IReadOnlyList<ShellCommand>> SearchAsync(
        string query,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        var providerTasks = _providers
            .Select(provider => QueryProviderAsync(provider, query, cancellationToken))
            .ToArray();

        var providerResults = await Task.WhenAll(providerTasks).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        return providerResults
            .SelectMany(static results => results)
            .GroupBy(static command => command.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(command => CommandRanker.Score(command, query))
                .First())
            .Select(command => new RankedCommand(command, CommandRanker.Score(command, query)))
            .Where(ranked => ranked.Score > 0)
            .OrderByDescending(static ranked => ranked.Score)
            .ThenBy(static ranked => ranked.Command.Title, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .Select(static ranked => ranked.Command)
            .ToArray();
    }

    private static async Task<IReadOnlyList<ShellCommand>> QueryProviderAsync(
        ILauncherCommandProvider provider,
        string query,
        CancellationToken cancellationToken)
    {
        try
        {
            return await provider.GetCommandsAsync(query, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return [];
        }
    }

    private sealed record RankedCommand(ShellCommand Command, int Score);
}
