namespace SeanShell.Plugin.Git;

public static class GitStatusParser
{
    public static GitRepositorySnapshot? Parse(string repositoryPath, string output)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);
        ArgumentNullException.ThrowIfNull(output);

        var lines = output
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0 || !lines[0].StartsWith("## ", StringComparison.Ordinal))
        {
            return null;
        }

        var header = lines[0][3..].Trim();
        var branch = ParseBranch(header);
        var trackingStatus = ParseTrackingStatus(header);
        var repositoryName = Path.GetFileName(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(repositoryPath)));

        return new GitRepositorySnapshot(
            Path.GetFullPath(repositoryPath),
            repositoryName,
            branch,
            lines.Length - 1,
            trackingStatus);
    }

    private static string ParseBranch(string header)
    {
        const string noCommitsPrefix = "No commits yet on ";
        const string initialCommitPrefix = "Initial commit on ";

        if (header.StartsWith(noCommitsPrefix, StringComparison.Ordinal))
        {
            return header[noCommitsPrefix.Length..];
        }

        if (header.StartsWith(initialCommitPrefix, StringComparison.Ordinal))
        {
            return header[initialCommitPrefix.Length..];
        }

        if (header.StartsWith("HEAD (no branch)", StringComparison.Ordinal))
        {
            return "detached HEAD";
        }

        var trackingIndex = header.IndexOf("...", StringComparison.Ordinal);
        return trackingIndex >= 0 ? header[..trackingIndex] : header;
    }

    private static string? ParseTrackingStatus(string header)
    {
        var start = header.LastIndexOf(" [", StringComparison.Ordinal);
        if (start < 0 || !header.EndsWith(']'))
        {
            return null;
        }

        return header[(start + 2)..^1];
    }
}
