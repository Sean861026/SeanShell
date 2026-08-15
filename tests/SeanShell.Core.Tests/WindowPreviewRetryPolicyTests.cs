using SeanShell.Core;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class WindowPreviewRetryPolicyTests
{
    [TestMethod]
    public void UnresolvedThumbnailRetriesAfterFirstArrangeAttempt()
    {
        Assert.IsTrue(WindowPreviewRetryPolicy.ShouldRetry(
            hasUnresolvedThumbnail: true,
            completedAttempts: 1));
        Assert.AreEqual(
            TimeSpan.FromMilliseconds(32),
            WindowPreviewRetryPolicy.Delay);
    }

    [TestMethod]
    public void SuccessfulThumbnailDoesNotRetry()
    {
        Assert.IsFalse(WindowPreviewRetryPolicy.ShouldRetry(
            hasUnresolvedThumbnail: false,
            completedAttempts: 1));
    }

    [TestMethod]
    public void PermanentFailureStopsAtBound()
    {
        Assert.IsFalse(WindowPreviewRetryPolicy.ShouldRetry(
            hasUnresolvedThumbnail: true,
            completedAttempts: WindowPreviewRetryPolicy.MaximumAttempts));
    }
}
