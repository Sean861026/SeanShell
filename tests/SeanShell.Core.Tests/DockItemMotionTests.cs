using SeanShell.Core;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class DockItemMotionTests
{
    [DataRow(false, false, 1.0f, 0.0f, 180)]
    [DataRow(true, false, 1.35f, -6.0f, 220)]
    [DataRow(true, true, 0.92f, 0.0f, 90)]
    [TestMethod]
    public void ResolveReturnsBoundedPointerFeedback(
        bool isPointerOver,
        bool isPressed,
        float expectedScale,
        float expectedTranslationY,
        int expectedDuration)
    {
        var result = DockItemMotion.Resolve(
            isPointerOver,
            isPressed,
            reducedEffects: false);

        Assert.AreEqual(expectedScale, result.Scale);
        Assert.AreEqual(expectedTranslationY, result.TranslationY);
        Assert.AreEqual(expectedDuration, result.DurationMilliseconds);
    }

    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    [TestMethod]
    public void ReducedEffectsDisableScaleTranslationAndAnimation(
        bool isPointerOver,
        bool isPressed)
    {
        Assert.AreEqual(
            new DockItemMotionState(1, 0, 0),
            DockItemMotion.Resolve(
                isPointerOver,
                isPressed,
                reducedEffects: true));
    }
}
