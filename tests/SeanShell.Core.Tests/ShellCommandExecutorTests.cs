using SeanShell.Core;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class ShellCommandExecutorTests
{
    [TestMethod]
    public async Task SynchronousCommandStartsOnCallerThread()
    {
        var callerThread = Environment.CurrentManagedThreadId;
        var command = Create(_ =>
        {
            Assert.AreEqual(callerThread, Environment.CurrentManagedThreadId);
            return ValueTask.CompletedTask;
        });

        await ShellCommandExecutor.ExecuteAsync(command);
    }

    [TestMethod]
    public async Task CommandWithoutDeadlineKeepsNormalCompletionSemantics()
    {
        var completion = NewCompletion();
        var execution = ShellCommandExecutor.ExecuteAsync(
            Create(_ => new ValueTask(completion.Task)) with { ExecutionTimeout = null }).AsTask();

        Assert.IsFalse(execution.IsCompleted);
        completion.SetResult();
        await execution;
    }

    [TestMethod]
    public async Task CommandFailureIsPreserved()
    {
        var expected = new InvalidOperationException("provider failure");
        var command = Create(_ => ValueTask.FromException(expected));

        var actual = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => ShellCommandExecutor.ExecuteAsync(command).AsTask());

        Assert.AreSame(expected, actual);
    }

    [TestMethod]
    public async Task PreCanceledRequestDoesNotDispatchCommand()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var dispatched = false;
        var command = Create(_ =>
        {
            dispatched = true;
            return ValueTask.CompletedTask;
        });

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => ShellCommandExecutor.ExecuteAsync(command, cancellation.Token).AsTask());
        Assert.IsFalse(dispatched);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    [DataRow(long.MaxValue)]
    public async Task InvalidDeadlineDoesNotDispatchCommand(long milliseconds)
    {
        var dispatched = false;
        var command = Create(_ =>
        {
            dispatched = true;
            return ValueTask.CompletedTask;
        }) with
        { ExecutionTimeout = milliseconds == long.MaxValue ? TimeSpan.MaxValue : TimeSpan.FromMilliseconds(milliseconds) };

        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(
            () => ShellCommandExecutor.ExecuteAsync(command).AsTask());
        Assert.IsFalse(dispatched);
    }

    [TestMethod]
    public async Task DeadlineReturnsAndCancelsUncooperativeCommand()
    {
        var completion = NewCompletion();
        CancellationToken receivedToken = default;
        var command = Create(token =>
        {
            receivedToken = token;
            return new ValueTask(completion.Task);
        }) with
        { ExecutionTimeout = TimeSpan.FromMilliseconds(50) };

        try
        {
            await Assert.ThrowsExactlyAsync<TimeoutException>(
                () => ShellCommandExecutor.ExecuteAsync(command).AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.IsTrue(receivedToken.IsCancellationRequested);
            Assert.IsFalse(completion.Task.IsCompleted);
        }
        finally
        {
            completion.TrySetException(new InvalidOperationException("late provider failure"));
        }
    }

    [TestMethod]
    public async Task CallerCancellationIsNotReportedAsTimeout()
    {
        using var cancellation = new CancellationTokenSource();
        var completion = NewCompletion();
        CancellationToken receivedToken = default;
        var command = Create(token =>
        {
            receivedToken = token;
            return new ValueTask(completion.Task);
        });
        var execution = ShellCommandExecutor.ExecuteAsync(command, cancellation.Token).AsTask();

        try
        {
            cancellation.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(
                () => execution.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.IsTrue(receivedToken.IsCancellationRequested);
        }
        finally
        {
            completion.TrySetResult();
        }
    }

    [TestMethod]
    public async Task BlockingCancellationCallbackCannotHoldLauncherBusy()
    {
        var completion = NewCompletion();
        var releaseCallback = NewCompletion();
        var enteredCallback = NewCompletion();
        CancellationTokenRegistration registration = default;
        var command = Create(token =>
        {
            registration = token.Register(() =>
            {
                enteredCallback.TrySetResult();
                releaseCallback.Task.GetAwaiter().GetResult();
            });
            return new ValueTask(completion.Task);
        }) with
        { ExecutionTimeout = TimeSpan.FromMilliseconds(50) };

        try
        {
            await Assert.ThrowsExactlyAsync<TimeoutException>(
                () => ShellCommandExecutor.ExecuteAsync(command).AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
            await enteredCallback.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.IsFalse(releaseCallback.Task.IsCompleted);
        }
        finally
        {
            releaseCallback.TrySetResult();
            completion.TrySetResult();
            await registration.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task TimeoutReleasesReopenedLauncherWithoutApplyingOldResult()
    {
        var lifetime = new LauncherOperationLifetime();
        lifetime.TryShow(out _);
        lifetime.TryBegin(out var operation);
        var completion = NewCompletion();
        var command = Create(_ => new ValueTask(completion.Task)) with
        {
            ExecutionTimeout = TimeSpan.FromMilliseconds(50),
        };
        var execution = ShellCommandExecutor.ExecuteAsync(command).AsTask();
        lifetime.Hide();
        lifetime.TryShow(out _);

        try
        {
            await Assert.ThrowsExactlyAsync<TimeoutException>(() => execution.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.IsFalse(lifetime.CanApply(operation));
            Assert.IsTrue(lifetime.Complete(operation));
            Assert.IsTrue(lifetime.TryBegin(out var next));
            Assert.IsFalse(lifetime.Complete(operation));
            Assert.IsTrue(lifetime.CanApply(next));
        }
        finally
        {
            completion.TrySetResult();
        }
    }

    private static ShellCommand Create(Func<CancellationToken, ValueTask> execute) =>
        new("test:refresh", "Refresh", null, execute)
        {
            ExecutionTimeout = TimeSpan.FromSeconds(30),
        };

    private static TaskCompletionSource NewCompletion() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
