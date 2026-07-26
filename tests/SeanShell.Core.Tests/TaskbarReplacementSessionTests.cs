using SeanShell.Core;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class TaskbarReplacementSessionTests
{
    [TestMethod]
    public void EnableStartsRecoveryGuardBeforeHidingTaskbars()
    {
        var calls = new List<string>();
        var controller = new StubController(calls);
        var guard = new StubGuard(calls);
        using var session = new TaskbarReplacementSession(controller, guard);

        var result = session.Enable();

        Assert.IsTrue(result.Success);
        Assert.IsTrue(session.IsEnabled);
        CollectionAssert.AreEqual(
            new[] { "guard", "hide" },
            calls.Take(2).ToArray());
    }

    [TestMethod]
    public void GuardFailureLeavesTaskbarsVisible()
    {
        var calls = new List<string>();
        var controller = new StubController(calls);
        var guard = new StubGuard(calls) { CanStart = false };
        using var session = new TaskbarReplacementSession(controller, guard);

        var result = session.Enable();

        Assert.IsFalse(result.Success);
        Assert.IsFalse(session.IsEnabled);
        CollectionAssert.AreEqual(
            new[] { "guard", "show" },
            calls.Take(2).ToArray());
    }

    [TestMethod]
    public void HideFailureRollsBackToVisibleTaskbars()
    {
        var calls = new List<string>();
        var controller = new StubController(calls) { HideSucceeds = false };
        using var session = new TaskbarReplacementSession(
            controller,
            new StubGuard(calls));

        var result = session.Enable();

        Assert.IsFalse(result.Success);
        Assert.IsFalse(session.IsEnabled);
        CollectionAssert.AreEqual(
            new[] { "guard", "hide", "show" },
            calls.Take(3).ToArray());
    }

    [TestMethod]
    public void LostGuardFailsSafeAndRestoresTaskbars()
    {
        var calls = new List<string>();
        var controller = new StubController(calls);
        var guard = new StubGuard(calls);
        using var session = new TaskbarReplacementSession(controller, guard);
        Assert.IsTrue(session.Enable().Success);
        guard.CanStart = false;
        guard.IsRunning = false;

        var result = session.EnsureHidden();

        Assert.IsFalse(result.Success);
        Assert.IsFalse(session.IsEnabled);
        Assert.AreEqual("show", calls[^1]);
    }

    [TestMethod]
    public void DisableAndDisposeRestoreTaskbars()
    {
        var calls = new List<string>();
        var controller = new StubController(calls);
        var guard = new StubGuard(calls);
        var session = new TaskbarReplacementSession(controller, guard);
        Assert.IsTrue(session.Enable().Success);

        var result = session.Disable();
        session.Dispose();

        Assert.IsTrue(result.Success);
        Assert.IsFalse(session.IsEnabled);
        Assert.AreEqual(2, calls.Count(static call => call == "show"));
        Assert.IsTrue(guard.Disposed);
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

        public TaskbarOperationResult HideAll()
        {
            calls.Add("hide");
            return new TaskbarOperationResult(
                HideSucceeds,
                2,
                HideSucceeds ? null : "hide failed");
        }

        public TaskbarOperationResult ShowAll()
        {
            calls.Add("show");
            return new TaskbarOperationResult(true, 2);
        }
    }

    private sealed class StubGuard(List<string> calls) :
        ITaskbarRecoveryGuard,
        IDisposable
    {
        public bool CanStart { get; set; } = true;

        public bool IsRunning { get; set; }

        public bool Disposed { get; private set; }

        public bool EnsureStarted(out string? error)
        {
            calls.Add("guard");
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
