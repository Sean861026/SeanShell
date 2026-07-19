using SeanShell.Core;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class SystemMetricsSnapshotTests
{
    [TestMethod]
    public void CalculatesUsedMemoryAndPercentage()
    {
        var snapshot = new SystemMetricsSnapshot(
            42,
            16_000,
            4_000,
            DateTimeOffset.UnixEpoch);

        Assert.AreEqual(12_000UL, snapshot.UsedPhysicalMemoryBytes);
        Assert.AreEqual(75d, snapshot.MemoryUsagePercent);
    }

    [TestMethod]
    public void HandlesMissingOrInconsistentMemoryValues()
    {
        var missing = new SystemMetricsSnapshot(0, 0, 0, DateTimeOffset.UnixEpoch);
        var inconsistent = new SystemMetricsSnapshot(0, 4_000, 8_000, DateTimeOffset.UnixEpoch);

        Assert.AreEqual(0d, missing.MemoryUsagePercent);
        Assert.AreEqual(0UL, inconsistent.UsedPhysicalMemoryBytes);
    }
}
