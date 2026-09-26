using SeanShell.Core;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class DockLaunchMotionTests
{
    [TestMethod]
    public void ResolveReturnsBoundedTwoStepPulse()
    {
        var result = DockLaunchMotion.Resolve(reducedEffects: false);

        Assert.AreEqual(420, result.DurationMilliseconds);
        CollectionAssert.AreEqual(
            new DockLaunchMotionFrame[]
            {
                new(0, 1),
                new(0.24f, 0.6f),
                new(0.50f, 1),
                new(0.72f, 0.8f),
                new(1, 1),
            },
            result.Frames.ToArray());
        Assert.IsTrue(
            result.Frames.Zip(result.Frames.Skip(1))
                .All(pair => pair.First.Progress < pair.Second.Progress));
        Assert.IsTrue(result.Frames.All(frame => frame.Opacity is >= 0 and <= 1));
    }

    [TestMethod]
    public void ReducedEffectsDisableLaunchFeedback()
    {
        var result = DockLaunchMotion.Resolve(reducedEffects: true);

        Assert.AreEqual(0, result.DurationMilliseconds);
        CollectionAssert.AreEqual(
            new DockLaunchMotionFrame[] { new(0, 1) },
            result.Frames.ToArray());
    }
}
