namespace SeanShell.Core;

public sealed record SystemMetricsSnapshot(
    double CpuUsagePercent,
    ulong TotalPhysicalMemoryBytes,
    ulong AvailablePhysicalMemoryBytes,
    DateTimeOffset ObservedAt)
{
    public ulong UsedPhysicalMemoryBytes =>
        TotalPhysicalMemoryBytes >= AvailablePhysicalMemoryBytes
            ? TotalPhysicalMemoryBytes - AvailablePhysicalMemoryBytes
            : 0;

    public double MemoryUsagePercent =>
        TotalPhysicalMemoryBytes == 0
            ? 0
            : UsedPhysicalMemoryBytes * 100d / TotalPhysicalMemoryBytes;
}
