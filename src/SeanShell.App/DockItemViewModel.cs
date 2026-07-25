using SeanShell.Core;

namespace SeanShell.App;

public sealed class DockItemViewModel(DesktopWindowSnapshot window)
{
    public nint Handle { get; } = window.Handle;

    public string ProcessName { get; } = window.ProcessName;

    public string Title { get; } = window.Title;

    public bool IsForeground { get; } = window.IsForeground;

    public bool IsMinimized { get; } = window.IsMinimized;

    public double ContentOpacity => IsMinimized ? 0.58 : 1;

    public string StateText => IsForeground
        ? "Active"
        : IsMinimized
            ? "Minimized"
            : "Running";

    public string AccessibleName => $"Switch to {Title}, {ProcessName}, {StateText}";
}
