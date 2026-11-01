using SeanShell.Core;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class ActivityIslandPresentationStateTests
{
    [TestMethod]
    public void EmptyStateHasNoDeadline()
    {
        var state = new ActivityIslandPresentationState();

        Assert.AreEqual(0, state.RemainingMilliseconds(0));
        Assert.AreEqual(0, state.RemainingMilliseconds(long.MaxValue));
    }

    [TestMethod]
    public void FirstMessageShowsForExactlyTheConfiguredLifetime()
    {
        var state = new ActivityIslandPresentationState();

        Assert.AreEqual(ActivityIslandPresentationChange.Show,
            state.Present(Activation(), 1, 100));
        Assert.AreEqual(ActivityIslandPresentationState.DismissDelayMilliseconds,
            state.RemainingMilliseconds(100));
        Assert.AreEqual(1, state.RemainingMilliseconds(2499));
        Assert.AreEqual(0, state.RemainingMilliseconds(2500));
        Assert.AreEqual(0, state.RemainingMilliseconds(10000));
    }

    [TestMethod]
    public void OldQueuedTickCannotExpireAReplacement()
    {
        var state = new ActivityIslandPresentationState();
        state.Present(Activation(), 1, 0);

        Assert.AreEqual(ActivityIslandPresentationChange.Update,
            state.Present(ActivityIslandContent.ForInputLayout("English"), 1, 2300));
        Assert.AreEqual(2300, state.RemainingMilliseconds(2400));
        Assert.AreEqual(1, state.RemainingMilliseconds(4699));
        Assert.AreEqual(0, state.RemainingMilliseconds(4700));
    }

    [TestMethod]
    public void EquivalentMessagesDoNotExtendTheDeadline()
    {
        var state = new ActivityIslandPresentationState();
        state.Present(Activation(), 1, 0);

        Assert.AreEqual(ActivityIslandPresentationChange.Unchanged,
            state.Present(Activation(), 1, 1000));
        Assert.AreEqual(ActivityIslandPresentationChange.Unchanged,
            state.Present(Activation(), 1, 2300));
        Assert.AreEqual(100, state.RemainingMilliseconds(2300));
        Assert.AreEqual(0, state.RemainingMilliseconds(2400));
    }

    [TestMethod]
    [DataRow("Different app", "Window title")]
    [DataRow("Test app", "Different title")]
    public void ChangedTitleOrDetailUpdatesVisibleContent(string processName, string title)
    {
        var state = new ActivityIslandPresentationState();
        state.Present(Activation(), 1, 0);

        Assert.AreEqual(ActivityIslandPresentationChange.Update,
            state.Present(ActivityIslandContent.ForActivation(processName, title), 1, 500));
        Assert.AreEqual(2400, state.RemainingMilliseconds(500));
    }

    [TestMethod]
    public void DifferentKindsWithIdenticalTextAreNotCoalesced()
    {
        var state = new ActivityIslandPresentationState();
        var input = ActivityIslandContent.ForInputLayout("English");
        var activation = ActivityIslandContent.ForActivation(input.Title, input.Detail);
        state.Present(activation, 1, 0);

        Assert.AreEqual(ActivityIslandPresentationChange.Update, state.Present(input, 1, 500));
        Assert.AreEqual(2400, state.RemainingMilliseconds(500));
    }

    [TestMethod]
    public void MovingToAnotherMonitorStartsANewEntrance()
    {
        var state = new ActivityIslandPresentationState();
        state.Present(Activation(), 1, 0);

        Assert.AreEqual(ActivityIslandPresentationChange.Show, state.Present(Activation(), 2, 500));
        Assert.AreEqual(2400, state.RemainingMilliseconds(500));
        Assert.AreEqual(ActivityIslandPresentationChange.Unchanged, state.Present(Activation(), 2, 600));
        Assert.AreEqual(2300, state.RemainingMilliseconds(600));
    }

    [TestMethod]
    public void SameMessageCanShowAgainAtTheExactExpiryBoundary()
    {
        var state = new ActivityIslandPresentationState();
        state.Present(Activation(), 1, 0);

        Assert.AreEqual(ActivityIslandPresentationChange.Show, state.Present(Activation(), 1, 2400));
        Assert.AreEqual(2400, state.RemainingMilliseconds(2400));
    }

    [TestMethod]
    public void ExpiredReplacementDoesNotReplayEarlierContent()
    {
        var state = new ActivityIslandPresentationState();
        state.Present(Activation(), 1, 0);
        state.Present(ActivityIslandContent.ForEnabledPreview(), 1, 500);

        Assert.AreEqual(0, state.RemainingMilliseconds(2900));
        Assert.AreEqual(0, state.RemainingMilliseconds(5000));
        Assert.AreEqual(ActivityIslandPresentationChange.Show, state.Present(Activation(), 1, 5000));
    }

    [TestMethod]
    public void DismissInvalidatesQueuedTicksAndAllowsTheSameMessageToShowAgain()
    {
        var state = new ActivityIslandPresentationState();
        state.Present(Activation(), 1, 0);

        state.Dismiss();
        state.Dismiss();

        Assert.AreEqual(0, state.RemainingMilliseconds(1000));
        Assert.AreEqual(ActivityIslandPresentationChange.Show, state.Present(Activation(), 1, 1000));
        Assert.AreEqual(1000, state.RemainingMilliseconds(2400));
    }

    [TestMethod]
    public void NullContentDoesNotReplaceTheCurrentMessage()
    {
        var state = new ActivityIslandPresentationState();
        state.Present(Activation(), 1, 0);

        Assert.ThrowsExactly<ArgumentNullException>(() => state.Present(null!, 1, 1000));
        Assert.AreEqual(1400, state.RemainingMilliseconds(1000));
        Assert.AreEqual(ActivityIslandPresentationChange.Unchanged, state.Present(Activation(), 1, 1000));
    }

    [TestMethod]
    public void ZeroMonitorDoesNotReplaceTheCurrentMessage()
    {
        var state = new ActivityIslandPresentationState();
        state.Present(Activation(), 1, 0);

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => state.Present(Activation(), 0, 1000));
        Assert.AreEqual(1400, state.RemainingMilliseconds(1000));
        Assert.AreEqual(ActivityIslandPresentationChange.Unchanged, state.Present(Activation(), 1, 1000));
    }

    [TestMethod]
    public void AnyNonzeroNativeMonitorHandleIsAccepted()
    {
        var state = new ActivityIslandPresentationState();

        Assert.AreEqual(ActivityIslandPresentationChange.Show, state.Present(Activation(), -1, 0));
        Assert.AreEqual(2400, state.RemainingMilliseconds(0));
    }

    [TestMethod]
    [DataRow(-1L)]
    [DataRow(long.MinValue)]
    public void NegativePresentationTimeIsRejectedWithoutMutation(long timestamp)
    {
        var state = new ActivityIslandPresentationState();
        state.Present(Activation(), 1, 0);

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => state.Present(Activation(), 1, timestamp));
        Assert.AreEqual(2400, state.RemainingMilliseconds(0));
    }

    [TestMethod]
    [DataRow(-1L)]
    [DataRow(long.MinValue)]
    public void NegativeQueryTimeIsRejected(long timestamp)
    {
        var state = new ActivityIslandPresentationState();

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => state.RemainingMilliseconds(timestamp));
    }

    [TestMethod]
    public void DeadlineSaturatesInsteadOfOverflowing()
    {
        var state = new ActivityIslandPresentationState();

        Assert.AreEqual(ActivityIslandPresentationChange.Show,
            state.Present(Activation(), 1, long.MaxValue - 1000));
        Assert.AreEqual(1000, state.RemainingMilliseconds(long.MaxValue - 1000));
        Assert.AreEqual(1, state.RemainingMilliseconds(long.MaxValue - 1));
        Assert.AreEqual(0, state.RemainingMilliseconds(long.MaxValue));
    }

    [TestMethod]
    public void MaximumTimestampCannotProduceANegativeInterval()
    {
        var state = new ActivityIslandPresentationState();

        Assert.AreEqual(ActivityIslandPresentationChange.Show,
            state.Present(Activation(), 1, long.MaxValue));
        Assert.AreEqual(0, state.RemainingMilliseconds(long.MaxValue));
    }

    [TestMethod]
    public void EarlierQueryCannotProduceAnIntervalLongerThanTheLifetime()
    {
        var state = new ActivityIslandPresentationState();
        state.Present(Activation(), 1, long.MaxValue - 1000);

        Assert.AreEqual(2400, state.RemainingMilliseconds(0));
    }

    private static ActivityIslandContent Activation() =>
        ActivityIslandContent.ForActivation("Test app", "Window title");
}
