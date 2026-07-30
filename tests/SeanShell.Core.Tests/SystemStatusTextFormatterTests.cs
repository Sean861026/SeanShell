using SeanShell.Core;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class SystemStatusTextFormatterTests
{
    [TestMethod]
    public void FormatsConnectedChargingBattery()
    {
        var text = SystemStatusTextFormatter.Format(
            new SystemStatusSnapshot(true, true, 84, true, true));

        Assert.AreEqual("Network & internet — Connected", text.Network);
        Assert.AreEqual("Power & battery — 84% · Charging", text.Power);
        StringAssert.Contains(
            text.AccessibleSummary,
            "battery",
            StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    public void FormatsDisconnectedBatteryPower()
    {
        var text = SystemStatusTextFormatter.Format(
            new SystemStatusSnapshot(false, true, 31, false, false));

        Assert.AreEqual("Network & internet — Disconnected", text.Network);
        Assert.AreEqual("Power & battery — 31% · On battery", text.Power);
    }

    [TestMethod]
    public void FormatsDesktopWithoutBattery()
    {
        var text = SystemStatusTextFormatter.Format(
            new SystemStatusSnapshot(true, false, null, true, false));

        Assert.AreEqual("Power & battery — Plugged in", text.Power);
    }

    [TestMethod]
    public void FormatsUnknownStatus()
    {
        var text = SystemStatusTextFormatter.Format(
            new SystemStatusSnapshot(null, true, null, null, false));

        Assert.AreEqual(
            "Network & internet — Status unavailable",
            text.Network);
        Assert.AreEqual(
            "Power & battery — Battery level unavailable",
            text.Power);
    }

    [TestMethod]
    public void DoesNotReportMissingBatteryWhenPowerCaptureFailed()
    {
        var text = SystemStatusTextFormatter.Format(
            new SystemStatusSnapshot(true, false, null, null, false));

        Assert.AreEqual("Power & battery — Status unavailable", text.Power);
    }

    [TestMethod]
    public void ClampsInvalidBatteryPercent()
    {
        var text = SystemStatusTextFormatter.Format(
            new SystemStatusSnapshot(true, true, 140, true, false));

        Assert.AreEqual("Power & battery — 100% · Plugged in", text.Power);
    }
}
