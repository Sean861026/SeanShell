using SeanShell.Core;

namespace SeanShell.App;

public sealed class LauncherResultViewModel(ShellCommand command)
{
    public ShellCommand Command { get; } = command;

    public string Title => Command.Title;

    public string Subtitle => Command.Subtitle ?? string.Empty;

    public string Glyph => Command.Glyph;

    public string KindLabel => Command.Kind switch
    {
        ShellCommandKind.Application => "App",
        ShellCommandKind.System => "System",
        _ => "Plugin",
    };
}
