using SeanShell.Core;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class DockPlacementTests
{
    private static readonly DisplayMonitorSnapshot SecondaryMonitor = new(
        2,
        "Secondary",
        -1920,
        0,
        1920,
        1040,
        false);

    [TestMethod]
    public void CentersExpandedDockInsideMonitorWorkArea()
    {
        var bounds = DockPlacement.Calculate(SecondaryMonitor, 760, 80, false, 160, 10);

        Assert.AreEqual(new DockBounds(-1340, 948, 760, 80), bounds);
    }

    [TestMethod]
    public void PlacesCollapsedIndicatorAtWorkAreaEdge()
    {
        var bounds = DockPlacement.Calculate(SecondaryMonitor, 760, 80, true, 160, 10);

        Assert.AreEqual(new DockBounds(-1040, 1030, 160, 10), bounds);
    }

    [TestMethod]
    public void ClampsDockToVerySmallWorkArea()
    {
        var monitor = new DisplayMonitorSnapshot(1, "Small", 0, 0, 100, 60, true);

        var bounds = DockPlacement.Calculate(monitor, 760, 80, false, 160, 10);

        Assert.AreEqual(new DockBounds(8, 0, 84, 60), bounds);
    }
}
