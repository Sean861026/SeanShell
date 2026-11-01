using SeanShell.Core;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class TaskbarReplacementSessionTests
{
    [TestMethod]
    public async Task EnableStartsRecoveryGuardBeforeHidingTaskbars()
    {
        var calls = new List<string>();
        var controller = new StubController(calls);
        var guard = new StubGuard(calls);
        await using var session = new TaskbarReplacementSession(controller, guard);

        var result = await session.EnableAsync();

        Assert.IsTrue(result.Success);
        Assert.IsTrue(session.IsEnabled);
        CollectionAssert.AreEqual(
            new[] { "guard", "hide" },
            calls.Take(2).ToArray());
    }

    [TestMethod]
    public async Task GuardFailureLeavesTaskbarsVisible()
    {
        var calls = new List<string>();
        var controller = new StubController(calls);
        var guard = new StubGuard(calls) { CanStart = false };
        await using var session = new TaskbarReplacementSession(controller, guard);

        var result = await session.EnableAsync();

        Assert.IsFalse(result.Success);
        Assert.IsFalse(session.IsEnabled);
        CollectionAssert.AreEqual(
            new[] { "guard", "show" },
            calls.Take(2).ToArray());
    }

    [TestMethod]
    public async Task HideFailureRollsBackToVisibleTaskbars()
    {
        var calls = new List<string>();
        var controller = new StubController(calls) { HideSucceeds = false };
        await using var session = new TaskbarReplacementSession(
            controller,
            new StubGuard(calls));

        var result = await session.EnableAsync();

        Assert.IsFalse(result.Success);
        Assert.IsFalse(session.IsEnabled);
        CollectionAssert.AreEqual(
            new[] { "guard", "hide", "show" },
            calls.Take(3).ToArray());
    }

    [TestMethod]
    public async Task LostGuardFailsSafeAndRestoresTaskbars()
    {
        var calls = new List<string>();
        var controller = new StubController(calls);
        var guard = new StubGuard(calls);
        await using var session = new TaskbarReplacementSession(controller, guard);
        Assert.IsTrue((await session.EnableAsync()).Success);
        guard.CanStart = false;
        guard.IsRunning = false;

        var result = await session.EnsureHiddenAsync();

        Assert.IsFalse(result.Success);
        Assert.IsFalse(session.IsEnabled);
        Assert.AreEqual("show", calls[^1]);
    }

    [TestMethod]
    public async Task DisableAndDisposeRestoreTaskbars()
    {
        var calls = new List<string>();
        var controller = new StubController(calls);
        var guard = new StubGuard(calls);
        var session = new TaskbarReplacementSession(controller, guard);
        Assert.IsTrue((await session.EnableAsync()).Success);

        var result = await session.DisableAsync();
        await session.DisposeAsync();

        Assert.IsTrue(result.Success);
        Assert.IsFalse(session.IsEnabled);
        Assert.AreEqual(2, calls.Count(static call => call == "show"));
        Assert.IsTrue(guard.Disposed);
    }

    [TestMethod]
    public async Task RevealShowsTaskbarsWithoutDisablingReplacementSession()
    {
        var calls = new List<string>();
        var controller = new StubController(calls);
        await using var session = new TaskbarReplacementSession(
            controller,
            new StubGuard(calls));
        Assert.IsTrue((await session.EnableAsync()).Success);

        var result = await session.RevealAsync();

        Assert.IsTrue(result.Success);
        Assert.IsTrue(session.IsEnabled);
        Assert.AreEqual("show", calls[^1]);
    }

    [TestMethod]
    public async Task RevealFailsWhenReplacementIsNotEnabled()
    {
        var calls = new List<string>();
        await using var session = new TaskbarReplacementSession(
            new StubController(calls),
            new StubGuard(calls));

        var result = await session.RevealAsync();

        Assert.IsFalse(result.Success);
        Assert.IsEmpty(calls);
    }

    [TestMethod]
    public async Task SlowGuardDoesNotBlockCallerAndDisablePreventsLateHide()
    {
        var calls = new List<string>();
        using var release = new ManualResetEventSlim();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var guard = new StubGuard(calls)
        {
            BeforeStart = () =>
            {
                started.SetResult();
                Assert.IsTrue(release.Wait(TimeSpan.FromSeconds(5)));
            },
        };
        await using var session = new TaskbarReplacementSession(new StubController(calls), guard);
        var enabling = session.EnableAsync();
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.IsFalse(enabling.IsCompleted);
            var disabling = session.DisableAsync();
            release.Set();
            Assert.IsTrue((await enabling).Superseded);
            Assert.IsTrue((await disabling).Success);
            Assert.IsFalse(session.IsEnabled);
            CollectionAssert.AreEqual(new[] { "guard", "show" }, calls);
        }
        finally
        {
            release.Set();
        }
    }

    [TestMethod]
    public async Task RevealSupersedesQueuedRefreshAndWaitsForInFlightHideCompensation()
    {
        var calls = new List<string>();
        using var release = new ManualResetEventSlim();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var controller = new StubController(calls);
        await using var session = new TaskbarReplacementSession(controller, new StubGuard(calls));
        Assert.IsTrue((await session.EnableAsync()).Success);
        controller.BeforeHide = () =>
        {
            started.SetResult();
            Assert.IsTrue(release.Wait(TimeSpan.FromSeconds(5)));
        };
        var refreshing = session.EnsureHiddenAsync();
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var queuedRefresh = session.EnsureHiddenAsync();
            var revealing = session.RevealAsync();
            release.Set();
            Assert.IsTrue((await refreshing).Superseded);
            Assert.IsTrue((await queuedRefresh).Superseded);
            Assert.IsTrue((await revealing).Success);
            Assert.IsTrue(session.IsEnabled);
            CollectionAssert.AreEqual(
                new[] { "guard", "hide", "guard", "hide", "show", "show" }, calls);
        }
        finally
        {
            release.Set();
        }
    }

    [TestMethod]
    public async Task ShutdownDrainsInFlightHideAndRejectsAllLaterHides()
    {
        var calls = new List<string>();
        using var release = new ManualResetEventSlim();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var controller = new StubController(calls)
        {
            BeforeHide = () =>
            {
                started.SetResult();
                Assert.IsTrue(release.Wait(TimeSpan.FromSeconds(5)));
            },
        };
        var guard = new StubGuard(calls);
        await using var session = new TaskbarReplacementSession(controller, guard);
        var enabling = session.EnableAsync();
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var queued = session.EnsureHiddenAsync();
            var shutdown = session.ShutdownAsync();
            Assert.IsTrue((await session.EnableAsync()).Superseded);
            Assert.IsTrue((await session.EnsureHiddenAsync()).Superseded);
            release.Set();
            Assert.IsTrue((await enabling).Superseded);
            Assert.IsTrue((await queued).Superseded);
            Assert.IsTrue((await shutdown).Success);
            Assert.AreSame(shutdown, session.ShutdownAsync());
            Assert.IsFalse(session.IsEnabled);
            Assert.IsTrue(guard.Disposed);
            CollectionAssert.AreEqual(new[] { "guard", "hide", "show", "show" }, calls);
        }
        finally
        {
            release.Set();
        }
    }

    [TestMethod]
    public async Task ControllerFaultRestoresTaskbarWithoutPoisoningQueue()
    {
        var calls = new List<string>();
        var controller = new StubController(calls)
        {
            BeforeHide = () => throw new InvalidOperationException("native failure"),
        };
        await using var session = new TaskbarReplacementSession(controller, new StubGuard(calls));
        var failed = await session.EnableAsync();
        Assert.IsFalse(failed.Success);
        StringAssert.Contains(failed.Error, "native failure");
        Assert.IsFalse(session.IsEnabled);
        Assert.AreEqual("show", calls[^1]);
        controller.BeforeHide = null;
        Assert.IsTrue((await session.EnableAsync()).Success);
    }

    [TestMethod]
    public async Task FailedRestorationIsNotReportedAsSuccess()
    {
        var calls = new List<string>();
        var controller = new StubController(calls) { ShowSucceeds = false };
        await using var session = new TaskbarReplacementSession(controller, new StubGuard(calls));
        Assert.IsTrue((await session.EnableAsync()).Success);
        var result = await session.DisableAsync();
        Assert.IsFalse(result.Success);
        Assert.IsFalse(session.IsEnabled);
        StringAssert.Contains(result.Error, "show failed");
        Assert.IsFalse((await session.ShutdownAsync()).Success);
    }

    [TestMethod]
    public void GuardArgumentsRequireExactPositiveOwnerPid()
    {
        Assert.IsTrue(TaskbarRecoveryArguments.TryParseOwnerProcessId(
            [TaskbarRecoveryArguments.GuardModeArgument, "123"],
            out var ownerProcessId));
        Assert.AreEqual(123, ownerProcessId);
        Assert.IsFalse(TaskbarRecoveryArguments.TryParseOwnerProcessId(
            [TaskbarRecoveryArguments.GuardModeArgument, "0"],
            out _));
        Assert.IsFalse(TaskbarRecoveryArguments.TryParseOwnerProcessId(
            [TaskbarRecoveryArguments.GuardModeArgument, "123", "extra"],
            out _));
        Assert.IsFalse(TaskbarRecoveryArguments.TryParseOwnerProcessId(
            ["--other-mode", "123"],
            out _));
    }

    private sealed class StubController(List<string> calls) : ITaskbarController
    {
        public bool HideSucceeds { get; init; } = true;
        public bool ShowSucceeds { get; init; } = true;
        public Action? BeforeHide { get; set; }

        public TaskbarOperationResult HideAll()
        {
            calls.Add("hide");
            BeforeHide?.Invoke();
            return new TaskbarOperationResult(
                HideSucceeds,
                2,
                HideSucceeds ? null : "hide failed");
        }

        public TaskbarOperationResult ShowAll()
        {
            calls.Add("show");
            return new TaskbarOperationResult(ShowSucceeds, 2, ShowSucceeds ? null : "show failed");
        }
    }

    private sealed class StubGuard(List<string> calls) :
        ITaskbarRecoveryGuard,
        IDisposable
    {
        public bool CanStart { get; set; } = true;

        public bool IsRunning { get; set; }

        public bool Disposed { get; private set; }
        public Action? BeforeStart { get; init; }

        public bool EnsureStarted(out string? error)
        {
            calls.Add("guard");
            BeforeStart?.Invoke();
            if (IsRunning)
            {
                error = null;
                return true;
            }

            if (!CanStart)
            {
                error = "guard failed";
                return false;
            }

            IsRunning = true;
            error = null;
            return true;
        }

        public void Dispose()
        {
            Disposed = true;
        }
    }
}
