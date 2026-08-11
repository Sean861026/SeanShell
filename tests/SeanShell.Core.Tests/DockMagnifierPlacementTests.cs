using SeanShell.Core;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class DockMagnifierPlacementTests
{
    private static readonly DisplayMonitorSnapshot Monitor = new(
        1,
        "DISPLAY1",
        -1920,
        0,
        1920,
        1080,
        false);

    [TestMethod]
    public void CentersAboveAnchorAndScalesToPhysicalPixels()
    {
        var result = DockMagnifierPlacement.Calculate(
            -960,
            1040,
            Monitor,
            96,
            112,
            1.25);

        Assert.AreEqual(new DockMagnifierBounds(-1020, 900, 120, 140), result);
    }

    [TestMethod]
    public void ConstrainsOverlayToMonitorWorkArea()
    {
        var result = DockMagnifierPlacement.Calculate(
            -1930,
            40,
            Monitor,
            96,
            112,
            1);

        Assert.AreEqual(new DockMagnifierBounds(-1920, 0, 96, 112), result);
    }

    [TestMethod]
    public void RejectsInvalidDimensionsAndScale()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            DockMagnifierPlacement.Calculate(0, 0, Monitor, 0, 112, 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            DockMagnifierPlacement.Calculate(0, 0, Monitor, 96, 112, 0));
    }
}
