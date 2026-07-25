using SeanShell.Core;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class LauncherPerformanceMonitorTests
{
    [TestMethod]
    public void RecordFirstUsable_KeepsOnlyFirstMeasurement()
    {
        var monitor = new LauncherPerformanceMonitor();

        monitor.RecordFirstUsable(TimeSpan.FromMilliseconds(120));
        monitor.RecordFirstUsable(TimeSpan.FromMilliseconds(80));

        Assert.AreEqual(TimeSpan.FromMilliseconds(120), monitor.Current.FirstUsableDuration);
    }

    [TestMethod]
    public void RecordSuccessfulSearch_ReportsLastAndNearestRankP95()
    {
        var monitor = new LauncherPerformanceMonitor();
        foreach (var milliseconds in Enumerable.Range(1, 20))
        {
            monitor.RecordSuccessfulSearch(TimeSpan.FromMilliseconds(milliseconds));
        }

        var snapshot = monitor.Current;

        Assert.AreEqual(20, snapshot.SuccessfulSearchCount);
        Assert.AreEqual(TimeSpan.FromMilliseconds(20), snapshot.LastSearchDuration);
        Assert.AreEqual(TimeSpan.FromMilliseconds(19), snapshot.P95SearchDuration);
    }

    [TestMethod]
    public void RecordSuccessfulSearch_KeepsMostRecentFiftyMeasurements()
    {
        var monitor = new LauncherPerformanceMonitor();
        foreach (var milliseconds in Enumerable.Range(1, 100))
        {
            monitor.RecordSuccessfulSearch(TimeSpan.FromMilliseconds(milliseconds));
        }

        var snapshot = monitor.Current;

        Assert.AreEqual(50, snapshot.SuccessfulSearchCount);
        Assert.AreEqual(TimeSpan.FromMilliseconds(100), snapshot.LastSearchDuration);
        Assert.AreEqual(TimeSpan.FromMilliseconds(98), snapshot.P95SearchDuration);
    }

    [TestMethod]
    public void Measurements_RaiseChanged()
    {
        var monitor = new LauncherPerformanceMonitor();
        var eventCount = 0;
        monitor.Changed += (_, _) => eventCount++;

        monitor.RecordFirstUsable(TimeSpan.FromMilliseconds(100));
        monitor.RecordSuccessfulSearch(TimeSpan.FromMilliseconds(10));

        Assert.AreEqual(2, eventCount);
    }
}
