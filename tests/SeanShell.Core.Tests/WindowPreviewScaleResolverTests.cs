using SeanShell.Core;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class WindowPreviewScaleResolverTests
{
    [TestMethod]
    public void MonitorScaleWinsWhenXamlRootReportsOne()
    {
        Assert.AreEqual(
            2.25,
            WindowPreviewScaleResolver.Resolve(
                displayScaleFactor: 2.25,
                xamlRootScaleFactor: 1));
    }

    [TestMethod]
    public void XamlRootScaleIsOnlyACompatibilityFallback()
    {
        Assert.AreEqual(
            1.5,
            WindowPreviewScaleResolver.Resolve(
                displayScaleFactor: double.NaN,
                xamlRootScaleFactor: 1.5));
    }

    [TestMethod]
    public void InvalidInputsFallBackToIdentity()
    {
        Assert.AreEqual(
            1,
            WindowPreviewScaleResolver.Resolve(
                displayScaleFactor: 0,
                xamlRootScaleFactor: null));
    }
}
