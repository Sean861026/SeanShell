using SeanShell.Core;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class ActivityIslandCleanupTests
{
    [TestMethod]
    public void SuccessfulCleanupRunsWithoutReporting()
    {
        var calls = 0;
        Assert.IsTrue(ActivityIslandCleanup.TryRun(() => calls++, _ => Assert.Fail()));
        Assert.AreEqual(1, calls);
    }

    [TestMethod]
    public void FailureReportsTheExceptionAndAllowsLaterCleanup()
    {
        var failure = new InvalidOperationException();
        Exception? reported = null;
        Assert.IsFalse(ActivityIslandCleanup.TryRun(() => throw failure, exception => reported = exception));
        Assert.AreSame(failure, reported);
        var closed = false;
        Assert.IsTrue(ActivityIslandCleanup.TryRun(() => closed = true));
        Assert.IsTrue(closed);
    }

    [TestMethod]
    public void DiagnosticFailureDoesNotEscapeCleanup()
    {
        Assert.IsFalse(ActivityIslandCleanup.TryRun(
            () => throw new InvalidOperationException(),
            _ => throw new ObjectDisposedException("diagnostics")));
    }

    [TestMethod]
    public void CleanupDoesNotSwallowOutOfMemory()
    {
        Assert.ThrowsExactly<OutOfMemoryException>(() =>
            ActivityIslandCleanup.TryRun(() => throw new OutOfMemoryException()));
    }

    [TestMethod]
    public void DiagnosticsDoNotSwallowOutOfMemory()
    {
        Assert.ThrowsExactly<OutOfMemoryException>(() => ActivityIslandCleanup.TryRun(
            () => throw new InvalidOperationException(),
            _ => throw new OutOfMemoryException()));
    }
}
