namespace SeanShell.Core;

public static class TaskbarWindowGrouper
{
    public static string GetKey(TaskbarWindowGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        return GetGroupKey(group.PrimaryWindow);
    }

    public static IReadOnlyList<TaskbarWindowGroup> Group(
        IReadOnlyList<DesktopWindowSnapshot> windows)
    {
        ArgumentNullException.ThrowIfNull(windows);

        return windows
            .GroupBy(
                static window => GetGroupKey(window),
                StringComparer.OrdinalIgnoreCase)
            .Select(static group => new TaskbarWindowGroup(
                group.First().ProcessName,
                group
                    .OrderByDescending(static window => window.IsForeground)
                    .ThenBy(static window => window.Title, StringComparer.OrdinalIgnoreCase)
                    .ToArray()))
            .OrderBy(static group => group.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string GetGroupKey(DesktopWindowSnapshot window) =>
        IsGenericHost(window.ProcessName)
            ? $"window:{window.Handle}"
            : $"process:{window.ProcessName}";

    private static bool IsGenericHost(string processName) =>
        string.IsNullOrWhiteSpace(processName) ||
        processName.Equals("Application", StringComparison.OrdinalIgnoreCase) ||
        processName.Equals("ApplicationFrameHost", StringComparison.OrdinalIgnoreCase) ||
        processName.Equals("RuntimeBroker", StringComparison.OrdinalIgnoreCase);
}
