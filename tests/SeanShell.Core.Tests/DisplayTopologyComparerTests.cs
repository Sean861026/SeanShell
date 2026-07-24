using SeanShell.Core;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class DisplayTopologyComparerTests
{
    private static readonly DisplayMonitorSnapshot Primary = new(
        1,
        @"\\.\DISPLAY1",
        0,
        0,
        1920,
        1040,
        true);

    private static readonly DisplayMonitorSnapshot Secondary = new(
        2,
        @"\\.\DISPLAY2",
        -1920,
        0,
        1920,
        1040,
        false);

    [TestMethod]
    public void EquivalentTopologyIgnoresEnumerationOrder()
    {
        Assert.IsTrue(DisplayTopologyComparer.AreEquivalent(
            [Primary, Secondary],
            [Secondary, Primary]));
    }

    [TestMethod]
    public void ChangedWorkAreaRequiresDockRebuild()
    {
        var resizedPrimary = Primary with { WorkAreaHeight = 1000 };

        Assert.IsFalse(DisplayTopologyComparer.AreEquivalent(
            [Primary, Secondary],
            [resizedPrimary, Secondary]));
    }

    [TestMethod]
    public void ChangedMonitorHandleRequiresDockRebuild()
    {
        var replacementSecondary = Secondary with { Handle = 3 };

        Assert.IsFalse(DisplayTopologyComparer.AreEquivalent(
            [Primary, Secondary],
            [Primary, replacementSecondary]));
    }

    [TestMethod]
    public void AddedOrRemovedMonitorRequiresDockRebuild()
    {
        Assert.IsFalse(DisplayTopologyComparer.AreEquivalent(
            [Primary],
            [Primary, Secondary]));
    }
}
