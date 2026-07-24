namespace SeanShell.Plugin.Git;

public sealed record GitRepositorySnapshot(
    string Path,
    string Name,
    string Branch,
    int ChangedFileCount,
    string? TrackingStatus = null)
{
    public string StatusText
    {
        get
        {
            var changes = ChangedFileCount == 0
                ? "clean"
                : $"{ChangedFileCount} change{(ChangedFileCount == 1 ? string.Empty : "s")}";
            return string.IsNullOrWhiteSpace(TrackingStatus)
                ? $"{Branch} \u00B7 {changes}"
                : $"{Branch} \u00B7 {changes} \u00B7 {TrackingStatus}";
        }
    }
}
