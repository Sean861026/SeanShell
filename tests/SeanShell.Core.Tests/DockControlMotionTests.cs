using SeanShell.Core;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class DockControlMotionTests
{
    [DataRow(false, false, 1.0f, 120)]
    [DataRow(true, false, 1.03f, 140)]
    [DataRow(true, true, 0.97f, 80)]
    [TestMethod]
    public void ResolveKeepsStatusControlsInsideTheirLane(
        bool isPointerOver,
        bool isPressed,
        float expectedScale,
        int expectedDuration)
    {
        var result = DockControlMotion.Resolve(
            isPointerOver,
            isPressed,
            reducedEffects: false);

        Assert.AreEqual(expectedScale, result.Scale);
        Assert.AreEqual(0, result.TranslationY);
        Assert.AreEqual(expectedDuration, result.DurationMilliseconds);
    }

    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    [TestMethod]
    public void ReducedEffectsDisableStatusControlMotion(
        bool isPointerOver,
        bool isPressed)
    {
        Assert.AreEqual(
            new DockItemMotionState(1, 0, 0),
            DockControlMotion.Resolve(
                isPointerOver,
                isPressed,
                reducedEffects: true));
    }
}
