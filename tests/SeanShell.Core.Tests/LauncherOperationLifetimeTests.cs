using SeanShell.Core;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class LauncherOperationLifetimeTests
{
    [TestMethod]
    public void HiddenLauncherCannotStartActions()
    {
        var lifetime = new LauncherOperationLifetime();
        Assert.IsFalse(lifetime.TryBegin(out _));
        Assert.IsFalse(lifetime.IsCurrent(lifetime.Session));
    }

    [TestMethod]
    public void VisibleSessionAllowsExactlyOneAction()
    {
        var lifetime = new LauncherOperationLifetime();
        Assert.IsTrue(lifetime.TryShow(out var session));
        Assert.IsTrue(lifetime.IsCurrent(session));
        Assert.IsTrue(lifetime.TryBegin(out var operation));
        Assert.IsTrue(lifetime.CanApply(operation));
        Assert.IsTrue(lifetime.IsBusy);
        Assert.IsFalse(lifetime.TryBegin(out _));
    }

    [TestMethod]
    public void CompletionReleasesGateAndInvalidatesTicket()
    {
        var lifetime = new LauncherOperationLifetime();
        lifetime.TryShow(out _);
        lifetime.TryBegin(out var first);
        Assert.IsTrue(lifetime.Complete(first));
        Assert.IsFalse(lifetime.IsBusy);
        Assert.IsFalse(lifetime.CanApply(first));
        Assert.IsFalse(lifetime.Complete(first));
        Assert.IsTrue(lifetime.TryBegin(out var second));
        Assert.AreNotEqual(first, second);
        Assert.IsFalse(lifetime.Complete(first));
        Assert.IsTrue(lifetime.IsBusy);
        Assert.IsTrue(lifetime.CanApply(second));
    }

    [TestMethod]
    public void HidingInvalidatesViewButKeepsDispatchedActionBusy()
    {
        var lifetime = new LauncherOperationLifetime();
        lifetime.TryShow(out var session);
        lifetime.TryBegin(out var operation);
        lifetime.Hide();
        Assert.IsFalse(lifetime.IsCurrent(session));
        Assert.IsFalse(lifetime.CanApply(operation));
        Assert.IsTrue(lifetime.IsBusy);
        Assert.IsFalse(lifetime.TryBegin(out _));
        Assert.IsTrue(lifetime.Complete(operation));
        Assert.IsFalse(lifetime.IsBusy);
    }

    [TestMethod]
    public void OldActionCannotCloseOrReportErrorsIntoReopenedLauncher()
    {
        var lifetime = new LauncherOperationLifetime();
        lifetime.TryShow(out var firstSession);
        lifetime.TryBegin(out var oldOperation);
        lifetime.Hide();
        lifetime.TryShow(out var newSession);
        Assert.AreNotEqual(firstSession, newSession);
        Assert.IsFalse(lifetime.IsCurrent(firstSession));
        Assert.IsTrue(lifetime.IsCurrent(newSession));
        Assert.IsFalse(lifetime.CanApply(oldOperation));
        Assert.IsFalse(lifetime.TryBegin(out _));
        Assert.IsTrue(lifetime.Complete(oldOperation));
        Assert.IsTrue(lifetime.IsVisible);
        Assert.IsTrue(lifetime.TryBegin(out var newOperation));
        Assert.IsTrue(lifetime.CanApply(newOperation));
    }

    [TestMethod]
    public void RepeatedShowInvalidatesPreviousSearchAndActionSession()
    {
        var lifetime = new LauncherOperationLifetime();
        lifetime.TryShow(out var firstSession);
        lifetime.TryBegin(out var operation);
        lifetime.TryShow(out var nextSession);
        Assert.IsFalse(lifetime.IsCurrent(firstSession));
        Assert.IsTrue(lifetime.IsCurrent(nextSession));
        Assert.IsFalse(lifetime.CanApply(operation));
        Assert.IsTrue(lifetime.IsBusy);
    }

    [TestMethod]
    public void ShutdownIsPermanentAndLateCompletionCannotUpdateView()
    {
        var lifetime = new LauncherOperationLifetime();
        lifetime.TryShow(out var session);
        lifetime.TryBegin(out var operation);
        lifetime.Shutdown();
        lifetime.Shutdown();
        lifetime.Hide();
        Assert.IsTrue(lifetime.IsShutdown);
        Assert.IsFalse(lifetime.IsVisible);
        Assert.IsFalse(lifetime.IsCurrent(session));
        Assert.IsFalse(lifetime.CanApply(operation));
        Assert.IsFalse(lifetime.TryShow(out _));
        Assert.IsFalse(lifetime.TryBegin(out _));
        Assert.IsTrue(lifetime.Complete(operation));
        Assert.IsFalse(lifetime.TryShow(out _));
    }

    [TestMethod]
    public void DefaultOrWrongTicketCannotReleaseActiveOperation()
    {
        var lifetime = new LauncherOperationLifetime();
        Assert.IsFalse(lifetime.Complete(default));
        Assert.IsFalse(lifetime.CanApply(default));
        lifetime.TryShow(out _);
        lifetime.TryBegin(out var operation);
        Assert.IsFalse(lifetime.Complete(operation with { Session = operation.Session + 1 }));
        Assert.IsFalse(lifetime.Complete(operation with { Id = operation.Id + 1 }));
        Assert.IsFalse(lifetime.Complete(default));
        Assert.IsTrue(lifetime.IsBusy);
        Assert.IsTrue(lifetime.CanApply(operation));
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task DelayedCompletionDoesNotPublishAfterReopenOrShutdown(
        bool shutdown, bool fail)
    {
        var lifetime = new LauncherOperationLifetime();
        lifetime.TryShow(out _);
        lifetime.TryBegin(out var operation);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var published = 0;
        var failures = 0;
        var execution = FinishAsync();

        if (shutdown)
        {
            lifetime.Shutdown();
        }
        else
        {
            lifetime.Hide();
            lifetime.TryShow(out _);
        }

        Assert.IsTrue(lifetime.IsBusy);
        Assert.IsFalse(lifetime.TryBegin(out _));
        completion.SetResult();
        await execution;

        Assert.AreEqual(0, published);
        Assert.AreEqual(fail ? 1 : 0, failures);
        Assert.IsFalse(lifetime.IsBusy);
        Assert.AreEqual(!shutdown, lifetime.IsVisible);

        async Task FinishAsync()
        {
            try
            {
                await completion.Task;
                if (fail)
                {
                    throw new InvalidOperationException("late provider failure");
                }

                if (lifetime.CanApply(operation))
                {
                    published++;
                    lifetime.Hide();
                }
            }
            catch (InvalidOperationException)
            {
                failures++;
                if (lifetime.CanApply(operation))
                {
                    published++;
                }
            }
            finally
            {
                lifetime.Complete(operation);
            }
        }
    }
}
