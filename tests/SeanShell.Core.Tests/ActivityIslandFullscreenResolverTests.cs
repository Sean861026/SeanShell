using SeanShell.Core;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class ActivityIslandFullscreenResolverTests
{
    [TestMethod]
    public void NormalTopApplicationPreventsBackgroundFullscreenSuppression()
    {
        var result = ActivityIslandFullscreenResolver.Resolve(
            [new(1, false), new(1, true)]);

        Assert.IsEmpty(result);
    }

    [TestMethod]
    public void FullscreenTopApplicationSuppressesItsMonitor()
    {
        var result = ActivityIslandFullscreenResolver.Resolve(
            [new(1, true), new(1, false)]);

        CollectionAssert.AreEqual(new nint[] { 1 }, result.ToArray());
    }

    [TestMethod]
    public void EachMonitorUsesItsOwnTopApplication()
    {
        var result = ActivityIslandFullscreenResolver.Resolve(
            [new(1, false), new(2, true), new(1, true), new(2, false), new(3, true)]);

        CollectionAssert.AreEqual(new nint[] { 2, 3 }, result.ToArray());
    }

    [TestMethod]
    public void ZeroMonitorIsIgnoredAndDuplicatesAreNotReturned()
    {
        var result = ActivityIslandFullscreenResolver.Resolve(
            [new(0, true), new(1, true), new(1, true)]);

        CollectionAssert.AreEqual(new nint[] { 1 }, result.ToArray());
    }

    [TestMethod]
    public void EmptyDesktopDoesNotSuppressAnyMonitor()
    {
        Assert.IsEmpty(ActivityIslandFullscreenResolver.Resolve([]));
    }

    [TestMethod]
    public void NullInventoryIsRejected()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => ActivityIslandFullscreenResolver.Resolve(null!));
    }
}
