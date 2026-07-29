namespace SeanShell.Core;

public enum DockShortcut
{
    ControlAltD,
    ControlShiftD,
}

public static class DockShortcutNames
{
    public static string GetDisplayName(this DockShortcut shortcut) => shortcut switch
    {
        DockShortcut.ControlAltD => "Ctrl + Alt + D",
        DockShortcut.ControlShiftD => "Ctrl + Shift + D",
        _ => throw new ArgumentOutOfRangeException(nameof(shortcut), shortcut, null),
    };
}
