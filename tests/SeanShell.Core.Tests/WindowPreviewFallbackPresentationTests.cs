using SeanShell.Core;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class WindowPreviewFallbackPresentationTests
{
    [TestMethod]
    public void AvailableThumbnailHidesFallback()
    {
        Assert.AreEqual(
            WindowPreviewFallbackState.Hidden,
            WindowPreviewFallbackPresentation.Resolve(
                thumbnailAvailable: true,
                retryScheduled: true));
    }

    [TestMethod]
    public void PendingRetryUsesNeutralLoadingState()
    {
        Assert.AreEqual(
            WindowPreviewFallbackState.Loading,
            WindowPreviewFallbackPresentation.Resolve(
                thumbnailAvailable: false,
                retryScheduled: true));
    }

    [TestMethod]
    public void ExhaustedRetryShowsUnavailableState()
    {
        Assert.AreEqual(
            WindowPreviewFallbackState.Unavailable,
            WindowPreviewFallbackPresentation.Resolve(
                thumbnailAvailable: false,
                retryScheduled: false));
    }
}
