using SeanShell.Core;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class WindowPreviewLayoutTests
{
    [TestMethod]
    [DataRow(0, 0, 0, 0)]
    [DataRow(1, 1, 1, 1)]
    [DataRow(2, 2, 1, 2)]
    [DataRow(3, 3, 1, 3)]
    [DataRow(4, 4, 2, 2)]
    [DataRow(6, 6, 2, 3)]
    [DataRow(9, 6, 2, 3)]
    public void CalculateUsesBoundedResponsiveGrid(
        int windowCount,
        int expectedVisibleCount,
        int expectedRows,
        int expectedColumns)
    {
        var result = WindowPreviewLayout.Calculate(windowCount);

        Assert.AreEqual(expectedVisibleCount, result.VisibleCount);
        Assert.AreEqual(expectedRows, result.Rows);
        Assert.AreEqual(expectedColumns, result.Columns);
    }

    [TestMethod]
    public void CalculateIncludesCardsGapsAndOuterPadding()
    {
        var result = WindowPreviewLayout.Calculate(4);

        Assert.AreEqual(
            (WindowPreviewLayout.OuterPadding * 2) +
            (WindowPreviewLayout.CardWidth * 2) +
            WindowPreviewLayout.Gap,
            result.Width);
        Assert.AreEqual(
            (WindowPreviewLayout.OuterPadding * 2) +
            (WindowPreviewLayout.CardHeight * 2) +
            WindowPreviewLayout.Gap,
            result.Height);
    }
}
