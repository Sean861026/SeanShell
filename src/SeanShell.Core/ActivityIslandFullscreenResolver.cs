namespace SeanShell.Core;

public sealed record ActivityIslandWindowCoverage(nint MonitorHandle, bool CoversMonitor);

public static class ActivityIslandFullscreenResolver
{
    /// <summary>
    /// The first eligible application in top-to-bottom Z order owns each
    /// monitor's policy. A covered application behind it cannot suppress an Island.
    /// </summary>
    public static IReadOnlyList<nint> Resolve(
        IEnumerable<ActivityIslandWindowCoverage> windowsInZOrder)
    {
        ArgumentNullException.ThrowIfNull(windowsInZOrder);
        var seenMonitors = new HashSet<nint>();
        var suppressedMonitors = new List<nint>();
        foreach (var window in windowsInZOrder)
        {
            if (window.MonitorHandle == 0 || !seenMonitors.Add(window.MonitorHandle))
            {
                continue;
            }

            if (window.CoversMonitor)
            {
                suppressedMonitors.Add(window.MonitorHandle);
            }
        }

        return suppressedMonitors;
    }
}
