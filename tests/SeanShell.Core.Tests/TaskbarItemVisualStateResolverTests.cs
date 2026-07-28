using SeanShell.Core;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class TaskbarItemVisualStateResolverTests
{
    [DataRow(true, false, TaskbarItemIndicator.Active, 1)]
    [DataRow(true, true, TaskbarItemIndicator.Active, 1)]
    [DataRow(false, false, TaskbarItemIndicator.Running, 1)]
    [DataRow(false, true, TaskbarItemIndicator.Minimized, 0.72)]
    [TestMethod]
    public void ResolveReturnsStableTaskbarPresentation(
        bool isForeground,
        bool isMinimized,
        TaskbarItemIndicator expectedIndicator,
        double expectedOpacity)
    {
        var state = TaskbarItemVisualStateResolver.Resolve(
            isForeground,
            isMinimized);

        Assert.AreEqual(expectedIndicator, state.Indicator);
        Assert.AreEqual(expectedOpacity, state.ContentOpacity);
    }
}
