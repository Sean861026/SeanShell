using SeanShell.Core;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class DockRefreshCoordinatorTests
{
    [TestMethod]
    public async Task PeriodicRefreshSkipsAnActiveScan()
    {
        var coordinator = new DockRefreshCoordinator();
        using var active = await coordinator.EnterAsync(explicitlyRequested: false);
        Assert.IsNotNull(active);

        Assert.IsNull(await coordinator.EnterAsync(explicitlyRequested: false));
        Assert.IsNull(await coordinator.EnterAsync(explicitlyRequested: false));
    }

    [TestMethod]
    public async Task ExplicitRefreshWaitsForCurrentScanInsteadOfSkipping()
    {
        var coordinator = new DockRefreshCoordinator();
        var active = await coordinator.EnterAsync(explicitlyRequested: false);
        Assert.IsNotNull(active);
        var requested = coordinator.EnterAsync(explicitlyRequested: true);

        Assert.IsFalse(requested.IsCompleted);
        Assert.IsNull(await coordinator.EnterAsync(explicitlyRequested: false));

        active.Dispose();
        using var manual = await requested.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.IsNotNull(manual);
        Assert.IsNull(await coordinator.EnterAsync(explicitlyRequested: false));
    }

    [TestMethod]
    public async Task MultipleExplicitRefreshesRunOneAtATime()
    {
        var coordinator = new DockRefreshCoordinator();
        var first = await coordinator.EnterAsync(explicitlyRequested: true);
        Assert.IsNotNull(first);
        var secondRequest = coordinator.EnterAsync(explicitlyRequested: true);
        var thirdRequest = coordinator.EnterAsync(explicitlyRequested: true);
        Assert.IsFalse(secondRequest.IsCompleted);
        Assert.IsFalse(thirdRequest.IsCompleted);

        first.Dispose();
        var second = await secondRequest.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.IsNotNull(second);
        Assert.IsFalse(thirdRequest.IsCompleted);
        second.Dispose();
        using var third = await thirdRequest.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.IsNotNull(third);
    }

    [TestMethod]
    public async Task ExitCancelsWaitingRequestWithoutReleasingActiveScan()
    {
        var coordinator = new DockRefreshCoordinator();
        var active = await coordinator.EnterAsync(explicitlyRequested: false);
        Assert.IsNotNull(active);
        using var shutdown = new CancellationTokenSource();
        var requested = coordinator.EnterAsync(explicitlyRequested: true, shutdown.Token);
        shutdown.Cancel();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () =>
            await requested);
        Assert.IsNull(await coordinator.EnterAsync(explicitlyRequested: false));
        active.Dispose();
        using var next = await coordinator.EnterAsync(explicitlyRequested: false);
        Assert.IsNotNull(next);
    }

    [TestMethod]
    public async Task DoubleDisposeDoesNotReleaseAnotherScan()
    {
        var coordinator = new DockRefreshCoordinator();
        var first = await coordinator.EnterAsync(explicitlyRequested: true);
        Assert.IsNotNull(first);
        first.Dispose();
        using var second = await coordinator.EnterAsync(explicitlyRequested: false);
        Assert.IsNotNull(second);
        first.Dispose();

        Assert.IsNull(await coordinator.EnterAsync(explicitlyRequested: false));
    }
}
