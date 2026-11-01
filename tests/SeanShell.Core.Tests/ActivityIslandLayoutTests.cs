using SeanShell.Core;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class ActivityIslandLayoutTests
{
    [TestMethod]
    [DataRow(1.0, 360, 76, 16)]
    [DataRow(1.5, 540, 114, 24)]
    [DataRow(2.0, 720, 152, 32)]
    public void PlacesScaledIslandAtTopCenter(double scale, int width, int height, int margin)
    {
        var monitor = Monitor(0, 0, 1920, 1080);

        var bounds = ActivityIslandLayout.Calculate(monitor, scale);

        Assert.AreEqual(new ActivityIslandBounds((1920 - width) / 2, margin, width, height), bounds);
        AssertContained(monitor, bounds);
    }

    [TestMethod]
    public void PreservesNegativeMonitorCoordinates()
    {
        var monitor = Monitor(-2560, -1440, 2560, 1400);

        var bounds = ActivityIslandLayout.Calculate(monitor, 1.5);

        Assert.AreEqual(new ActivityIslandBounds(-1550, -1416, 540, 114), bounds);
        AssertContained(monitor, bounds);
    }

    [TestMethod]
    [DataRow(1.0, 1.5, 114)]
    [DataRow(1.0, 2.0, 152)]
    [DataRow(1.5, 1.5, 171)]
    [DataRow(1.5, 2.0, 228)]
    [DataRow(2.0, 1.5, 228)]
    [DataRow(2.0, 2.0, 304)]
    [DataRow(1.0, 0.5, 76)]
    public void TextScalingGrowsHeightWithoutChangingWidthOrPlacement(
        double scale, double textScale, int expectedHeight)
    {
        var monitor = Monitor(-1920, -200, 1920, 1080);
        var baseline = ActivityIslandLayout.Calculate(monitor, scale);

        var bounds = ActivityIslandLayout.Calculate(monitor, scale, textScale);

        Assert.AreEqual(expectedHeight, bounds.Height);
        Assert.AreEqual(baseline.Width, bounds.Width);
        Assert.AreEqual(baseline.X, bounds.X);
        Assert.AreEqual(baseline.Y, bounds.Y);
        AssertContained(monitor, bounds);
    }

    [TestMethod]
    [DataRow(1, 1)]
    [DataRow(20, 10)]
    [DataRow(800, 200)]
    public void BoundsTextScaledHeightToAvailableWorkArea(int width, int height)
    {
        var monitor = Monitor(-10, -20, width, height);

        var bounds = ActivityIslandLayout.Calculate(monitor, 2, 2);

        AssertContained(monitor, bounds);
    }

    [TestMethod]
    public void BoundsVeryLargeFiniteTextScaleBeforeConvertingToPixels()
    {
        var monitor = Monitor(0, 0, 1920, 1080);

        var bounds = ActivityIslandLayout.Calculate(monitor, 2, double.MaxValue);

        Assert.AreEqual(720, bounds.Width);
        Assert.AreEqual(1016, bounds.Height);
        AssertContained(monitor, bounds);
    }

    [TestMethod]
    [DataRow(1, 1)]
    [DataRow(10, 20)]
    [DataRow(20, 10)]
    [DataRow(32, 32)]
    [DataRow(100, 50)]
    [DataRow(800, 1)]
    [DataRow(1, 600)]
    public void ShrinksContentAndMarginsInsideTinyWorkAreas(int width, int height)
    {
        var monitor = Monitor(-30, -40, width, height);

        var bounds = ActivityIslandLayout.Calculate(monitor, 2);

        AssertContained(monitor, bounds);
        Assert.IsLessThanOrEqualTo(1, Math.Abs(
            (bounds.X - monitor.WorkAreaX) -
            (monitor.WorkAreaWidth - bounds.Width - (bounds.X - monitor.WorkAreaX))));
    }

    [TestMethod]
    public void BoundsUnusuallyLargeFiniteScaleBeforeConvertingToPixels()
    {
        var monitor = Monitor(int.MinValue, int.MinValue, int.MaxValue, int.MaxValue);

        var bounds = ActivityIslandLayout.Calculate(monitor, double.MaxValue);

        AssertContained(monitor, bounds);
    }

    [TestMethod]
    public void VerySmallPositiveScaleStillProducesVisiblePhysicalPixel()
    {
        var monitor = Monitor(0, 0, 800, 600);

        var bounds = ActivityIslandLayout.Calculate(monitor, double.Epsilon);

        Assert.AreEqual(1, bounds.Width);
        Assert.AreEqual(1, bounds.Height);
        AssertContained(monitor, bounds);
    }

    [TestMethod]
    public void RejectsNullMonitor()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => ActivityIslandLayout.Calculate(null!, 1));
    }

    [TestMethod]
    [DataRow(0, 100)]
    [DataRow(-1, 100)]
    [DataRow(100, 0)]
    [DataRow(100, -1)]
    public void RejectsInvalidWorkArea(int width, int height)
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            ActivityIslandLayout.Calculate(Monitor(0, 0, width, height), 1));
    }

    [TestMethod]
    [DataRow(0.0)]
    [DataRow(-1.0)]
    [DataRow(double.NaN)]
    [DataRow(double.PositiveInfinity)]
    [DataRow(double.NegativeInfinity)]
    public void RejectsInvalidScale(double scale)
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            ActivityIslandLayout.Calculate(Monitor(0, 0, 800, 600), scale));
    }

    [TestMethod]
    [DataRow(0.0)]
    [DataRow(-1.0)]
    [DataRow(double.NaN)]
    [DataRow(double.PositiveInfinity)]
    [DataRow(double.NegativeInfinity)]
    public void RejectsInvalidTextScale(double textScale)
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            ActivityIslandLayout.Calculate(Monitor(0, 0, 800, 600), 1, textScale));
    }

    [TestMethod]
    public void RejectsCoordinatesThatCannotFitInNativeRectangle()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            ActivityIslandLayout.Calculate(Monitor(int.MaxValue, 0, 2, 1), 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            ActivityIslandLayout.Calculate(Monitor(0, int.MaxValue, 1, 2), 1));
    }

    private static DisplayMonitorSnapshot Monitor(int x, int y, int width, int height) =>
        new(1, "Test display", x, y, width, height, true);

    private static void AssertContained(DisplayMonitorSnapshot monitor, ActivityIslandBounds bounds)
    {
        Assert.IsGreaterThan(0, bounds.Width);
        Assert.IsGreaterThan(0, bounds.Height);
        Assert.IsGreaterThanOrEqualTo(monitor.WorkAreaX, bounds.X);
        Assert.IsGreaterThanOrEqualTo(monitor.WorkAreaY, bounds.Y);
        Assert.IsLessThanOrEqualTo((long)monitor.WorkAreaX + monitor.WorkAreaWidth,
            (long)bounds.X + bounds.Width);
        Assert.IsLessThanOrEqualTo((long)monitor.WorkAreaY + monitor.WorkAreaHeight,
            (long)bounds.Y + bounds.Height);
    }
}
