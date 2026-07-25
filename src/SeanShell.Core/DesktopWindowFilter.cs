namespace SeanShell.Core;

public static class DesktopWindowFilter
{
    public static IReadOnlyList<DesktopWindowSnapshot> ForMonitor(
        IEnumerable<DesktopWindowSnapshot> windows,
        nint monitorHandle,
        int maximumCount = 12)
    {
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCount);

        return windows
            .Where(window => window.MonitorHandle == monitorHandle)
            .Take(maximumCount)
            .ToArray();
    }
}
