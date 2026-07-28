namespace SeanShell.Core;

public static class TaskbarPinWindowMatcher
{
    public static bool IsMatch(
        ShellCommand pinnedApplication,
        DesktopWindowSnapshot window)
    {
        ArgumentNullException.ThrowIfNull(pinnedApplication);
        ArgumentNullException.ThrowIfNull(window);

        var pinnedProcessName = NormalizeProcessName(
            pinnedApplication.ApplicationProcessName);
        var windowProcessName = NormalizeProcessName(window.ProcessName);
        return pinnedProcessName.Length > 0 &&
               pinnedProcessName.Equals(
                   windowProcessName,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeProcessName(string? processName)
    {
        var trimmed = processName?.Trim() ?? string.Empty;
        return trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? trimmed[..^4]
            : trimmed;
    }
}
