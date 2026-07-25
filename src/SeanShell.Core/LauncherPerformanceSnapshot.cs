namespace SeanShell.Core;

public sealed record LauncherPerformanceSnapshot(
    TimeSpan? FirstUsableDuration,
    TimeSpan? LastSearchDuration,
    TimeSpan? P95SearchDuration,
    int SuccessfulSearchCount);
