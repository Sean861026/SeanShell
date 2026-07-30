using SeanShell.Core;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class DisplayScaleLayoutTests
{
    [TestMethod]
    [DataRow(420, 1.0, 420)]
    [DataRow(420, 1.25, 525)]
    [DataRow(420, 1.5, 630)]
    public void ConvertsDeviceIndependentUnitsToPhysicalPixels(
        int deviceIndependentPixels,
        double scaleFactor,
        int expected)
    {
        Assert.AreEqual(
            expected,
            DisplayScaleLayout.ToPhysicalPixels(
                deviceIndependentPixels,
                scaleFactor));
    }

    [TestMethod]
    [DataRow(1920, 1.0, 1920)]
    [DataRow(2752, 1.25, 2201)]
    [DataRow(1707, 1.5, 1138)]
    public void ConvertsPhysicalPixelsToDeviceIndependentUnits(
        int physicalPixels,
        double scaleFactor,
        int expected)
    {
        Assert.AreEqual(
            expected,
            DisplayScaleLayout.ToDeviceIndependentPixels(
                physicalPixels,
                scaleFactor));
    }

    [TestMethod]
    public void RejectsInvalidScaleFactors()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => DisplayScaleLayout.ToPhysicalPixels(420, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => DisplayScaleLayout.ToDeviceIndependentPixels(1920, double.NaN));
    }
}
