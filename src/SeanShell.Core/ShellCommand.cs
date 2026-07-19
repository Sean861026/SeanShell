namespace SeanShell.Core;

public sealed record ShellCommand(
    string Id,
    string Title,
    string? Subtitle,
    Func<CancellationToken, ValueTask> ExecuteAsync);
