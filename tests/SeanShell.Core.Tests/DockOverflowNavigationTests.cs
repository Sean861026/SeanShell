using SeanShell.Core;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class DockOverflowNavigationTests
{
    [TestMethod]
    [DataRow(0, 0, false, false, false)]
    [DataRow(0, 120, true, false, true)]
    [DataRow(60, 120, true, true, true)]
    [DataRow(120, 120, true, true, false)]
    public void ResolvesVisibleNavigationState(
        double offset,
        double scrollableWidth,
        bool isVisible,
        bool canNavigatePrevious,
        bool canNavigateNext)
    {
        Assert.AreEqual(
            new DockOverflowState(
                isVisible,
                canNavigatePrevious,
                canNavigateNext),
            DockOverflowNavigation.Resolve(offset, scrollableWidth));
    }

    [TestMethod]
    [DataRow(0, 200, 500, DockOverflowDirection.Next, 150)]
    [DataRow(450, 200, 500, DockOverflowDirection.Next, 500)]
    [DataRow(200, 200, 500, DockOverflowDirection.Previous, 50)]
    [DataRow(20, 40, 500, DockOverflowDirection.Previous, 0)]
    public void CalculatesBoundedPageTargets(
        double offset,
        double viewportWidth,
        double scrollableWidth,
        DockOverflowDirection direction,
        double expected)
    {
        Assert.AreEqual(
            expected,
            DockOverflowNavigation.CalculateTargetOffset(
                offset,
                viewportWidth,
                scrollableWidth,
                direction));
    }
}
