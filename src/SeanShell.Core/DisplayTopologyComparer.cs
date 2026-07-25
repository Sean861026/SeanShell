namespace SeanShell.Core;

public static class DisplayTopologyComparer
{
    public static bool AreEquivalent(
        IReadOnlyList<DisplayMonitorSnapshot> first,
        IReadOnlyList<DisplayMonitorSnapshot> second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        if (first.Count != second.Count)
        {
            return false;
        }

        return Normalize(first).SequenceEqual(Normalize(second));
    }

    private static IEnumerable<DisplayMonitorSnapshot> Normalize(
        IReadOnlyList<DisplayMonitorSnapshot> monitors) =>
        monitors
            .OrderBy(static monitor => monitor.DeviceName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static monitor => monitor.Handle)
            .ThenBy(static monitor => monitor.WorkAreaX)
            .ThenBy(static monitor => monitor.WorkAreaY)
            .ThenBy(static monitor => monitor.WorkAreaWidth)
            .ThenBy(static monitor => monitor.WorkAreaHeight)
            .ThenByDescending(static monitor => monitor.IsPrimary);
}
