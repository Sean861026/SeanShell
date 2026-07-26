using SeanShell.Gaming;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class GamingDetectionPerformanceMonitorTests
{
    [TestMethod]
    public void RecordSampleReportsLastP95AndEstimatedCpu()
    {
        var monitor = new GamingDetectionPerformanceMonitor();
        foreach (var milliseconds in Enumerable.Range(1, 20))
        {
            monitor.RecordSample(
                TimeSpan.FromMilliseconds(milliseconds),
                TimeSpan.FromMilliseconds(2),
                TimeSpan.FromSeconds(2),
                matchedProcessCount: milliseconds == 20 ? 1 : 0);
        }

        var snapshot = monitor.Current;

        Assert.AreEqual(20, snapshot.SampleCount);
        Assert.AreEqual(TimeSpan.FromMilliseconds(20), snapshot.LastScanDuration);
        Assert.AreEqual(TimeSpan.FromMilliseconds(19), snapshot.P95ScanDuration);
        Assert.AreEqual(1, snapshot.LastMatchedProcessCount);
        Assert.IsNotNull(snapshot.EstimatedCpuPercentage);
        Assert.IsGreaterThan(0, snapshot.EstimatedCpuPercentage.Value);
    }

    [TestMethod]
    public void RecordSampleKeepsMostRecentSixtyMeasurements()
    {
        var monitor = new GamingDetectionPerformanceMonitor();
        foreach (var milliseconds in Enumerable.Range(1, 100))
        {
            monitor.RecordSample(
                TimeSpan.FromMilliseconds(milliseconds),
                TimeSpan.Zero,
                TimeSpan.FromSeconds(2),
                matchedProcessCount: 0);
        }

        var snapshot = monitor.Current;

        Assert.AreEqual(60, snapshot.SampleCount);
        Assert.AreEqual(TimeSpan.FromMilliseconds(100), snapshot.LastScanDuration);
        Assert.AreEqual(TimeSpan.FromMilliseconds(97), snapshot.P95ScanDuration);
    }

    [TestMethod]
    public void ResetClearsMeasurementsAndRaisesChanged()
    {
        var monitor = new GamingDetectionPerformanceMonitor();
        var changedCount = 0;
        monitor.Changed += (_, _) => changedCount++;
        monitor.RecordSample(
            TimeSpan.FromMilliseconds(1),
            TimeSpan.Zero,
            TimeSpan.FromSeconds(2),
            matchedProcessCount: 0);

        monitor.Reset();

        Assert.AreEqual(0, monitor.Current.SampleCount);
        Assert.AreEqual(2, changedCount);
    }
}
