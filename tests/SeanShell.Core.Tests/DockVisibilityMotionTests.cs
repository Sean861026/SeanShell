using SeanShell.Core;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class DockVisibilityMotionTests
{
    [TestMethod]
    public void ExpandUsesResponsiveEaseOutTiming()
    {
        Assert.AreEqual(
            new DockVisibilityMotionState(0, 1, 12, 0, 180),
            DockVisibilityMotion.Resolve(
                collapsed: false,
                reducedEffects: false));
    }

    [TestMethod]
    public void CollapseExitsFasterThanExpand()
    {
        var expanded = DockVisibilityMotion.Resolve(
            collapsed: false,
            reducedEffects: false);
        var collapsed = DockVisibilityMotion.Resolve(
            collapsed: true,
            reducedEffects: false);

        Assert.AreEqual(
            new DockVisibilityMotionState(1, 0, 0, 12, 120),
            collapsed);
        Assert.IsLessThan(
expanded.DurationMilliseconds, collapsed.DurationMilliseconds);
    }

    [DataRow(false)]
    [DataRow(true)]
    [TestMethod]
    public void ReducedEffectsDisableVisibilityMotion(bool collapsed)
    {
        Assert.AreEqual(
            new DockVisibilityMotionState(1, 1, 0, 0, 0),
            DockVisibilityMotion.Resolve(
                collapsed,
                reducedEffects: true));
    }
}
