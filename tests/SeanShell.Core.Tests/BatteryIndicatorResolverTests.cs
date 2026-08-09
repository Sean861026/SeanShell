using SeanShell.Core;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class BatteryIndicatorResolverTests
{
    [TestMethod]
    public void NoBatteryUsesStableFullOutline()
    {
        var result = BatteryIndicatorResolver.Resolve(
            new SystemStatusSnapshot(true, false, null, true, false));

        Assert.AreEqual(BatteryIndicatorKind.NoBattery, result.Kind);
        Assert.AreEqual(10, result.Level);
        Assert.AreEqual(BatteryIndicatorEmphasis.Normal, result.Emphasis);
    }

    [TestMethod]
    public void MissingBatteryLevelIsUnavailable()
    {
        var result = BatteryIndicatorResolver.Resolve(
            new SystemStatusSnapshot(true, true, null, null, false));

        Assert.AreEqual(BatteryIndicatorKind.Unavailable, result.Kind);
        Assert.AreEqual(BatteryIndicatorEmphasis.Unavailable, result.Emphasis);
    }

    [TestMethod]
    public void ChargingBatteryUsesRoundedUpLevel()
    {
        var result = BatteryIndicatorResolver.Resolve(
            new SystemStatusSnapshot(true, true, 42, true, true));

        Assert.AreEqual(BatteryIndicatorKind.Charging, result.Kind);
        Assert.AreEqual(5, result.Level);
        Assert.AreEqual(BatteryIndicatorEmphasis.Charging, result.Emphasis);
    }

    [DataRow(10, BatteryIndicatorEmphasis.Critical)]
    [DataRow(25, BatteryIndicatorEmphasis.Caution)]
    [DataRow(80, BatteryIndicatorEmphasis.Normal)]
    [TestMethod]
    public void BatteryLevelMapsToBoundedEmphasis(
        int percent,
        BatteryIndicatorEmphasis expected)
    {
        var result = BatteryIndicatorResolver.Resolve(
            new SystemStatusSnapshot(true, true, percent, false, false));

        Assert.AreEqual(BatteryIndicatorKind.Battery, result.Kind);
        Assert.AreEqual(expected, result.Emphasis);
    }

    [TestMethod]
    public void OutOfRangeLevelIsClamped()
    {
        var result = BatteryIndicatorResolver.Resolve(
            new SystemStatusSnapshot(true, true, 140, false, false));

        Assert.AreEqual(10, result.Level);
    }
}
