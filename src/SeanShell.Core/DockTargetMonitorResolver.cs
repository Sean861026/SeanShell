namespace SeanShell.Core;

public static class DockTargetMonitorResolver
{
    public static int Resolve(
        IReadOnlyList<DisplayMonitorSnapshot> monitors,
        nint preferredMonitorHandle)
    {
        ArgumentNullException.ThrowIfNull(monitors);

        if (preferredMonitorHandle != 0)
        {
            for (var index = 0; index < monitors.Count; index++)
            {
                if (monitors[index].Handle == preferredMonitorHandle)
                {
                    return index;
                }
            }
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
}
