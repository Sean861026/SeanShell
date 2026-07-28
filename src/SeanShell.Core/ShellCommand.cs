namespace SeanShell.Core;

public sealed record ShellCommand(
    string Id,
    string Title,
    string? Subtitle,
    Func<CancellationToken, ValueTask> ExecuteAsync)
{
    public ShellCommandKind Kind { get; init; } = ShellCommandKind.Application;

    public IReadOnlyList<string> Keywords { get; init; } = [];

    public string Glyph { get; init; } = "\uE8B7";

    public string? IconSourcePath { get; init; }

    public ApplicationIconSnapshot? Icon { get; init; }
}
