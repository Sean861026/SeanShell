using SeanShell.Core;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class LauncherWindowPlacementTests
{
    [TestMethod]
    public void ScalesEffectiveSizeIntoPhysicalPixels()
    {
        var placement = LauncherWindowPlacement.Calculate(
            0,
            0,
            1920,
            1040,
            760,
            620,
            1.25);

        Assert.AreEqual(950, placement.Width);
        Assert.AreEqual(775, placement.Height);
        Assert.AreEqual(485, placement.X);
        Assert.AreEqual(88, placement.Y);
    }

    [TestMethod]
    public void PreservesNegativeOriginForSecondaryDisplay()
    {
        var placement = LauncherWindowPlacement.Calculate(
            -2560,
            0,
            2560,
            1400,
            760,
            620,
            1.5);

        Assert.AreEqual(-1850, placement.X);
        Assert.AreEqual(156, placement.Y);
        Assert.AreEqual(1140, placement.Width);
        Assert.AreEqual(930, placement.Height);
    }

    [TestMethod]
    public void ConstrainsWindowToSmallWorkAreaWithMargins()
    {
        var placement = LauncherWindowPlacement.Calculate(
            100,
            50,
            800,
            600,
            760,
            620,
            1.25);

        Assert.AreEqual(740, placement.Width);
        Assert.AreEqual(540, placement.Height);
        Assert.AreEqual(130, placement.X);
        Assert.AreEqual(80, placement.Y);
    }

    [TestMethod]
    public void RejectsInvalidWorkArea()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => LauncherWindowPlacement.Calculate(0, 0, 0, 600, 760, 620, 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => LauncherWindowPlacement.Calculate(0, 0, 800, -1, 760, 620, 1));
    }
}
