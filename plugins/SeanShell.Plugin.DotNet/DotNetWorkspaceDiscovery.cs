namespace SeanShell.Plugin.DotNet;

public static class DotNetWorkspaceDiscovery
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
        int maximumDepth = 4,
        int maximumWorkspaces = 24,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumDepth);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumWorkspaces, 1);
        cancellationToken.ThrowIfCancellationRequested();

        var workspaces = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots
            .Where(static root => !string.IsNullOrWhiteSpace(root))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            DiscoverRoot(root, maximumDepth, maximumWorkspaces, workspaces, cancellationToken);
            if (workspaces.Count >= maximumWorkspaces)
            {
                break;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return workspaces
            .OrderBy(static path => Path.GetExtension(path).ToLowerInvariant()
                is ".sln" or ".slnx" ? 0 : 1)
            .ThenBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void DiscoverRoot(
        string root,
        int maximumDepth,
        int maximumWorkspaces,
        HashSet<string> workspaces,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(root))
        {
            return;
        }

        var pending = new Queue<(string Path, int Depth)>();
        pending.Enqueue((root, 0));
        while (pending.Count > 0 && workspaces.Count < maximumWorkspaces)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = pending.Dequeue();
            foreach (var file in EnumerateWorkspaceFiles(
                candidate.Path,
                maximumWorkspaces - workspaces.Count,
                cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                workspaces.Add(Path.GetFullPath(file));
                if (workspaces.Count >= maximumWorkspaces)
                {
                    break;
                }
            }

            if (candidate.Depth >= maximumDepth)
            {
                continue;
            }

            foreach (var directory in EnumerateDirectories(candidate.Path, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!ShouldSkip(directory))
                {
                    pending.Enqueue((directory, candidate.Depth + 1));
                }
            }
        }
    }

    private static IEnumerable<string> EnumerateWorkspaceFiles(
        string path,
        int maximumFiles,
        CancellationToken cancellationToken)
    {
        var files = new List<string>();
        try
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Path.GetExtension(file).ToLowerInvariant() is ".sln" or ".slnx" or ".csproj")
                {
                    files.Add(file);
                    if (files.Count >= maximumFiles)
                    {
                        break;
                    }
                }
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
        }

        return files;
    }

    private static IEnumerable<string> EnumerateDirectories(
        string path,
        CancellationToken cancellationToken)
    {
        var directories = new List<string>();
        try
        {
            foreach (var directory in Directory.EnumerateDirectories(path))
            {
                cancellationToken.ThrowIfCancellationRequested();
                directories.Add(directory);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
        }

        return directories;
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
