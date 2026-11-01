using SeanShell.Core;
using SeanShell.Plugin.DotNet;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class DotNetWorkspaceRefreshTests
{
    private static readonly TimeSpan TestDeadline = TimeSpan.FromSeconds(10);

    [TestMethod]
    public async Task RefreshCommandReturnsToCallerBeforeBlockingScanCompletes()
    {
        using var release = new ManualResetEventSlim();
        var entered = NewSignal();
        var returned = new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);
        var callerThread = 0;
        var scannerThread = 0;
        await using var plugin = new DotNetWorkspacePlugin([], (_, _) =>
        {
            scannerThread = Environment.CurrentManagedThreadId;
            entered.TrySetResult();
            release.Wait();
            return [];
        });
        var refresh = await GetRefreshCommandAsync(plugin);
        // A dedicated caller makes the ordering assertion independent of whether
        // the test runner itself happens to be using the thread pool.
        var caller = Task.Factory.StartNew(() =>
        {
            callerThread = Environment.CurrentManagedThreadId;
            try
            {
                returned.TrySetResult(refresh.ExecuteAsync(CancellationToken.None).AsTask());
            }
            catch (Exception exception)
            {
                returned.TrySetException(exception);
            }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        try
        {
            await entered.Task.WaitAsync(TestDeadline);
            var pending = await returned.Task.WaitAsync(TestDeadline);
            Assert.IsFalse(pending.IsCompleted);
            Assert.AreNotEqual(callerThread, scannerThread);
        }
        finally
        {
            release.Set();
            await caller.WaitAsync(TestDeadline);
            await (await returned.Task.WaitAsync(TestDeadline)).WaitAsync(TestDeadline);
        }
    }

    [TestMethod]
    public async Task RefreshSerializesWorkersAndCanceledWaiterNeverScans()
    {
        using var release = new ManualResetEventSlim();
        using var canceledWaiter = new CancellationTokenSource();
        var entered = NewSignal();
        var scans = 0;
        var active = 0;
        var overlapped = false;
        await using var plugin = new DotNetWorkspacePlugin([], (_, _) =>
        {
            if (Interlocked.Increment(ref active) != 1)
            {
                overlapped = true;
            }

            try
            {
                if (Interlocked.Increment(ref scans) == 1)
                {
                    entered.TrySetResult();
                    release.Wait();
                }

                return [];
            }
            finally
            {
                Interlocked.Decrement(ref active);
            }
        });
        var refresh = await GetRefreshCommandAsync(plugin);
        var first = refresh.ExecuteAsync(CancellationToken.None).AsTask();
        Task? next = null;
        try
        {
            await entered.Task.WaitAsync(TestDeadline);
            var canceled = refresh.ExecuteAsync(canceledWaiter.Token).AsTask();
            next = refresh.ExecuteAsync(CancellationToken.None).AsTask();
            canceledWaiter.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(
                () => canceled.WaitAsync(TestDeadline));
            Assert.AreEqual(1, Volatile.Read(ref scans));
            Assert.IsFalse(next.IsCompleted);
        }
        finally
        {
            release.Set();
            await first.WaitAsync(TestDeadline);
            if (next is not null)
            {
                await next.WaitAsync(TestDeadline);
            }
        }

        Assert.AreEqual(2, scans);
        Assert.IsFalse(overlapped);
    }

    [TestMethod]
    public async Task CanceledWorkerCannotPublishAndNextRefreshCanSucceed()
    {
        using var release = new ManualResetEventSlim();
        using var cancellation = new CancellationTokenSource();
        var entered = NewSignal();
        var scans = 0;
        CancellationToken workerToken = default;
        await using var plugin = new DotNetWorkspacePlugin([], (_, token) =>
        {
            var scan = Interlocked.Increment(ref scans);
            if (scan == 2)
            {
                workerToken = token;
                entered.TrySetResult();
                // Simulate an OS read that finishes despite cancellation. The
                // refresh must keep its gate and refuse this stale result.
                release.Wait();
            }

            return [Snapshot(scan == 1 ? "Original" : scan == 2 ? "Canceled" : "Current")];
        });
        await plugin.InitializeAsync(CancellationToken.None);
        var refresh = await GetRefreshCommandAsync(plugin);
        var pending = refresh.ExecuteAsync(cancellation.Token).AsTask();
        try
        {
            await entered.Task.WaitAsync(TestDeadline);
            cancellation.Cancel();
            Assert.IsTrue(workerToken.IsCancellationRequested);
            Assert.IsFalse(pending.IsCompleted);
            Assert.IsTrue((await plugin.GetCommandsAsync(string.Empty, CancellationToken.None))
                .Any(command => command.Title == "Original"));
        }
        finally
        {
            release.Set();
        }

        await Assert.ThrowsAsync<OperationCanceledException>(() => pending.WaitAsync(TestDeadline));
        var preserved = await plugin.GetCommandsAsync(string.Empty, CancellationToken.None);
        Assert.IsTrue(preserved.Any(command => command.Title == "Original"));
        Assert.IsFalse(preserved.Any(command => command.Title == "Canceled"));
        await refresh.ExecuteAsync(CancellationToken.None);
        Assert.IsTrue((await plugin.GetCommandsAsync(string.Empty, CancellationToken.None))
            .Any(command => command.Title == "Current"));
    }

    [TestMethod]
    public async Task DisposalCancelsScanAndWaitersAndWaitsForWorkerExit()
    {
        using var release = new ManualResetEventSlim();
        var entered = NewSignal();
        var canceled = NewSignal();
        var scans = 0;
        var plugin = new DotNetWorkspacePlugin([], (_, token) =>
        {
            Interlocked.Increment(ref scans);
            using var registration = token.Register(() => canceled.TrySetResult());
            entered.TrySetResult();
            release.Wait();
            return [Snapshot("Must not publish")];
        });
        var refresh = await GetRefreshCommandAsync(plugin);
        var active = refresh.ExecuteAsync(CancellationToken.None).AsTask();
        Task? disposal = null;
        Task? waiter = null;
        try
        {
            await entered.Task.WaitAsync(TestDeadline);
            waiter = refresh.ExecuteAsync(CancellationToken.None).AsTask();
            disposal = plugin.DisposeAsync().AsTask();
            await canceled.Task.WaitAsync(TestDeadline);
            Assert.IsFalse(disposal.IsCompleted);
            Assert.AreSame(disposal, plugin.DisposeAsync().AsTask());
            await Assert.ThrowsAsync<OperationCanceledException>(() => waiter.WaitAsync(TestDeadline));
        }
        finally
        {
            release.Set();
            await Assert.ThrowsAsync<OperationCanceledException>(() => active.WaitAsync(TestDeadline));
            await (disposal ?? plugin.DisposeAsync().AsTask()).WaitAsync(TestDeadline);
        }

        Assert.AreEqual(1, scans);
        Assert.ThrowsExactly<ObjectDisposedException>(() =>
            plugin.GetCommandsAsync(string.Empty, CancellationToken.None));
        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(async () =>
            await refresh.ExecuteAsync(CancellationToken.None));
    }

    [TestMethod]
    public void DiscoveryInspectorAndLaunchSettingsHonorAlreadyCanceledRequests()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() =>
            DotNetWorkspaceDiscovery.Discover([], cancellationToken: cancellation.Token));
        Assert.ThrowsExactly<OperationCanceledException>(() =>
            DotNetWorkspaceInspector.Inspect("Missing.csproj", cancellation.Token));
        Assert.ThrowsExactly<OperationCanceledException>(() =>
            DotNetLaunchSettingsReader.ReadLocalApplicationUrls("Missing", cancellation.Token));
    }

    [TestMethod]
    public void ParserStreamChecksCancellationBetweenReads()
    {
        using var cancellation = new CancellationTokenSource();
        using var stream = new CancellationReadStream(new MemoryStream([1, 2]), cancellation.Token);
        var buffer = new byte[1];
        Assert.AreEqual(1, stream.Read(buffer, 0, buffer.Length));
        cancellation.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() => stream.Read(buffer, 0, buffer.Length));
        Assert.AreEqual(1L, stream.Position);
    }

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task<ShellCommand> GetRefreshCommandAsync(DotNetWorkspacePlugin plugin) =>
        (await plugin.GetCommandsAsync(string.Empty, CancellationToken.None))
            .Single(command => command.Title == "Refresh .NET workspaces");

    private static DotNetWorkspaceSnapshot Snapshot(string name) =>
        new(Path.Combine(Path.GetTempPath(), $"{name}.csproj"), name, "C#", [], false, false, false, []);
}
