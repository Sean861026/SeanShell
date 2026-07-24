namespace SeanShell.Plugin.Git;

public static class GitRepositoryDiscovery
{
    private static readonly HashSet<string> ExcludedDirectoryNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".git",
            ".vs",
            "bin",
            "node_modules",
            "obj",
            "packages",
        };

    public static IReadOnlyList<string> Discover(
        IEnumerable<string> roots,
        int maximumDepth = 2,
        int maximumRepositories = 12,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumDepth);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumRepositories, 1);

        var repositories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots
            .Where(static root => !string.IsNullOrWhiteSpace(root))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            DiscoverRoot(
                root,
                maximumDepth,
                maximumRepositories,
                repositories,
                cancellationToken);
            if (repositories.Count >= maximumRepositories)
            {
                break;
            }
        }

        return repositories
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void DiscoverRoot(
        string root,
        int maximumDepth,
        int maximumRepositories,
        HashSet<string> repositories,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(root))
        {
            return;
        }

        var pending = new Queue<(string Path, int Depth)>();
        pending.Enqueue((root, 0));
        while (pending.Count > 0 && repositories.Count < maximumRepositories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = pending.Dequeue();
            if (IsRepository(candidate.Path))
            {
                repositories.Add(Path.GetFullPath(candidate.Path));
                continue;
            }

            if (candidate.Depth >= maximumDepth)
            {
                continue;
            }

            foreach (var directory in EnumerateDirectories(candidate.Path))
            {
                if (!ShouldSkip(directory))
                {
                    pending.Enqueue((directory, candidate.Depth + 1));
                }
            }
        }
    }

    private static bool IsRepository(string path) =>
        Directory.Exists(Path.Combine(path, ".git")) ||
        File.Exists(Path.Combine(path, ".git"));

    private static IEnumerable<string> EnumerateDirectories(string path)
    {
        try
        {
            return Directory.EnumerateDirectories(path).ToArray();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static bool ShouldSkip(string path)
    {
        if (ExcludedDirectoryNames.Contains(Path.GetFileName(path)))
        {
            return true;
        }

        try
        {
            return File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }
}
