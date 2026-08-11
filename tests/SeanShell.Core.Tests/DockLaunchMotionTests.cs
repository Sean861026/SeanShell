using SeanShell.Core;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class DockLaunchMotionTests
{
    [TestMethod]
    public void ResolveReturnsBoundedTwoStepBounce()
    {
        var result = DockLaunchMotion.Resolve(reducedEffects: false);

        Assert.AreEqual(420, result.DurationMilliseconds);
        CollectionAssert.AreEqual(
            new DockLaunchMotionFrame[]
            {
                new(0, 0),
                new(0.24f, -14),
                new(0.50f, 0),
                new(0.72f, -7),
                new(1, 0),
            },
            result.Frames.ToArray());
        Assert.IsTrue(
            result.Frames.Zip(result.Frames.Skip(1))
                .All(pair => pair.First.Progress < pair.Second.Progress));
    }

    [TestMethod]
    public void ReducedEffectsDisableLaunchFeedback()
    {
        var result = DockLaunchMotion.Resolve(reducedEffects: true);

        Assert.AreEqual(0, result.DurationMilliseconds);
        CollectionAssert.AreEqual(
            new DockLaunchMotionFrame[] { new(0, 0) },
            result.Frames.ToArray());
    }
}
