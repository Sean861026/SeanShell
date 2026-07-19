using SeanShell.Core;

namespace SeanShell.App;

public sealed class DockItemViewModel(DesktopWindowSnapshot window)
{
    public nint Handle { get; } = window.Handle;

    public string ProcessName { get; } = window.ProcessName;

    public string Title { get; } = window.Title;

    public string AccessibleName => $"Switch to {Title}, {ProcessName}";
}
