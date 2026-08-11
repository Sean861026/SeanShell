namespace SeanShell.Core;

public static class LauncherTargetMonitorResolver
{
    public static int Resolve(
        IReadOnlyList<DisplayMonitorSnapshot> monitors,
        nint requestedMonitorHandle,
        nint foregroundMonitorHandle)
    {
        ArgumentNullException.ThrowIfNull(monitors);

        var requestedIndex = FindByHandle(monitors, requestedMonitorHandle);
        if (requestedIndex >= 0)
        {
            return requestedIndex;
        }

        var foregroundIndex = FindByHandle(monitors, foregroundMonitorHandle);
        if (foregroundIndex >= 0)
        {
            return foregroundIndex;
        }

        for (var index = 0; index < monitors.Count; index++)
        {
            if (monitors[index].IsPrimary)
            {
                return index;
            }
        }

        return monitors.Count > 0 ? 0 : -1;
    }

    private static int FindByHandle(
        IReadOnlyList<DisplayMonitorSnapshot> monitors,
        nint monitorHandle)
    {
        if (monitorHandle == 0)
        {
            return -1;
        }

        for (var index = 0; index < monitors.Count; index++)
        {
            if (monitors[index].Handle == monitorHandle)
            {
                return index;
            }
        }

        return -1;
    }
}
