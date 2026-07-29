using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using SeanShell.Core;

namespace SeanShell.App;

public sealed class PinnedDockItemViewModel(ShellCommand command)
{
    public ShellCommand Command { get; } = command;

    public string Id => Command.Id;

    public string Title => Command.Title;

    public string Glyph => Command.Glyph;

    public ImageSource? Icon { get; } = ApplicationIconSourceCache.Get(command.Icon);

    public Visibility IconVisibility =>
        Icon is null ? Visibility.Collapsed : Visibility.Visible;

    public Visibility FallbackIconVisibility =>
        Icon is null ? Visibility.Visible : Visibility.Collapsed;

    public string ToolTipText => $"{Title}\nPinned · Drag to reorder";

    public string AccessibleName => $"Open pinned application {Title}";
}
