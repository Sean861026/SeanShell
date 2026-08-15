namespace SeanShell.Core;

public static class WindowPreviewRetryPolicy
{
    public const int MaximumAttempts = 8;

    public static TimeSpan Delay { get; } = TimeSpan.FromMilliseconds(32);

    public static bool ShouldRetry(
        bool hasUnresolvedThumbnail,
        int completedAttempts) =>
        hasUnresolvedThumbnail &&
        completedAttempts < MaximumAttempts;
}
