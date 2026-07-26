using SeanShell.Core;

namespace SeanShell.App;

public sealed class PinnedDockItemViewModel(ShellCommand command)
{
    public ShellCommand Command { get; } = command;

    public string Id => Command.Id;

    public string Title => Command.Title;

    public string Glyph => Command.Glyph;

    public string AccessibleName => $"Open pinned application {Title}";
}
