using SeanShell.Core;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class WindowPreviewAspectFitTests
{
    [TestMethod]
    public void FitCentersWideSourceInsideTallTarget()
    {
        var result = WindowPreviewAspectFit.Fit(
            1920,
            1080,
            new WindowPreviewRectangle(10, 20, 240, 160));

        Assert.AreEqual(new WindowPreviewRectangle(10, 32, 240, 135), result);
    }

    [TestMethod]
    public void FitCentersTallSourceInsideWideTarget()
    {
        var result = WindowPreviewAspectFit.Fit(
            900,
            1600,
            new WindowPreviewRectangle(10, 20, 240, 160));

        Assert.AreEqual(new WindowPreviewRectangle(85, 20, 90, 160), result);
    }

    [TestMethod]
    public void FitReturnsTargetForInvalidSource()
    {
        var target = new WindowPreviewRectangle(10, 20, 240, 160);

        Assert.AreEqual(target, WindowPreviewAspectFit.Fit(0, 0, target));
    }
}
