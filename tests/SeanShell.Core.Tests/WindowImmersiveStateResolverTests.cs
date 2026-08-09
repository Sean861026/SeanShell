using SeanShell.Core;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class WindowImmersiveStateResolverTests
{
    private static readonly DockBounds Monitor = new(0, 0, 2560, 1440);

    [TestMethod]
    public void MaximizedWindowIsImmersiveEvenWhenWorkAreaIsReserved()
    {
        var result = WindowImmersiveStateResolver.IsImmersive(
            true,
            new DockBounds(0, 0, 2560, 1320),
            Monitor);

        Assert.IsTrue(result);
    }

    [TestMethod]
    public void BorderlessWindowCoveringMonitorIsImmersive()
    {
        var result = WindowImmersiveStateResolver.IsImmersive(
            false,
            new DockBounds(-1, -1, 2562, 1442),
            Monitor);

        Assert.IsTrue(result);
    }

    [TestMethod]
    public void OrdinaryWindowDoesNotSuppressDock()
    {
        var result = WindowImmersiveStateResolver.IsImmersive(
            false,
            new DockBounds(80, 60, 1600, 900),
            Monitor);

        Assert.IsFalse(result);
    }

    [TestMethod]
    public void InvalidBoundsDoNotSuppressDock()
    {
        var result = WindowImmersiveStateResolver.IsImmersive(
            false,
            new DockBounds(0, 0, 0, 0),
            Monitor);

        Assert.IsFalse(result);
    }
}
