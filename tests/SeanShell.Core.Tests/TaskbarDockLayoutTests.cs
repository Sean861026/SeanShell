using SeanShell.Core;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class TaskbarDockLayoutTests
{
    [DataRow(0, 0, 1920, 420)]
    [DataRow(0, 3, 1920, 480)]
    [DataRow(2, 4, 1920, 636)]
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

    [TestMethod]
    [DataRow(0, 3, 1920, 1.0, 480)]
    [DataRow(0, 3, 1920, 1.5, 545)]
    [DataRow(2, 4, 1920, 2.25, 798)]
    [DataRow(8, 10, 800, 2.25, 768)]
    public void CalculateExpandedWidthReservesScaledFixedControls(
        int pinnedItemCount,
        int windowItemCount,
        int monitorWorkAreaWidth,
        double textScaleFactor,
        int expected)
    {
        Assert.AreEqual(
            expected,
            TaskbarDockLayout.CalculateExpandedWidth(
                pinnedItemCount,
                windowItemCount,
                monitorWorkAreaWidth,
                textScaleFactor));
    }
}
