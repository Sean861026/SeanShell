namespace SeanShell.Core;

public enum LauncherShortcut
{
    AltSpace,
    ControlAltSpace,
    ControlShiftSpace,
}

public static class LauncherShortcutNames
{
    public static string GetDisplayName(this LauncherShortcut shortcut) => shortcut switch
    {
        LauncherShortcut.AltSpace => "Alt + Space",
        LauncherShortcut.ControlAltSpace => "Ctrl + Alt + Space",
        LauncherShortcut.ControlShiftSpace => "Ctrl + Shift + Space",
        _ => throw new ArgumentOutOfRangeException(nameof(shortcut), shortcut, null),
    };
}
