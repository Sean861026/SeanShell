namespace SeanShell.Core;

public sealed record ShellState(ShellMode Mode, DateTimeOffset ChangedAt)
{
    public static ShellState Initial { get; } = new(ShellMode.Normal, DateTimeOffset.UnixEpoch);
}
