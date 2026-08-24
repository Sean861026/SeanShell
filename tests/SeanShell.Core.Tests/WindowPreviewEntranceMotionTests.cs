using SeanShell.Core;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class WindowPreviewEntranceMotionTests
{
    [TestMethod]
    public void StandardMotionUsesShortUpwardEaseOut()
    {
        Assert.AreEqual(
            new WindowPreviewEntranceMotionState(0, 1, 8, 0, 160),
            WindowPreviewEntranceMotion.Resolve(reducedEffects: false));
    }

    [TestMethod]
    public void ReducedEffectsShowImmediately()
    {
        Assert.AreEqual(
            new WindowPreviewEntranceMotionState(1, 1, 0, 0, 0),
            WindowPreviewEntranceMotion.Resolve(reducedEffects: true));
    }
}
