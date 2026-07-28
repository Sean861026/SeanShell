using SeanShell.Core;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class TaskbarDockLayoutTests
{
    [DataRow(0, 0, 1920, 420)]
    [DataRow(0, 3, 1920, 420)]
    [DataRow(2, 4, 1920, 568)]
    [DataRow(8, 10, 1920, 1000)]
    [DataRow(8, 10, 800, 768)]
    [DataRow(0, 0, 300, 268)]
    [TestMethod]
    public void CalculateExpandedWidthUsesContentWithinMonitorBounds(
        int pinnedItemCount,
        int windowItemCount,
        int monitorWorkAreaWidth,
        int expected)
    {
        Assert.AreEqual(
            expected,
            TaskbarDockLayout.CalculateExpandedWidth(
                pinnedItemCount,
                windowItemCount,
                monitorWorkAreaWidth));
    }

    [TestMethod]
    public void CalculateExpandedWidthRejectsInvalidInputs()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => TaskbarDockLayout.CalculateExpandedWidth(-1, 0, 1920));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => TaskbarDockLayout.CalculateExpandedWidth(0, -1, 1920));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => TaskbarDockLayout.CalculateExpandedWidth(0, 0, 0));
    }
}
