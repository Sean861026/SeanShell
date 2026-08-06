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

    [DataRow(false, 1.0f, 0.0f, 160)]
    [DataRow(true, 1.14f, -2.0f, 180)]
    [TestMethod]
    public void ResolveNeighborReturnsSubordinateMagnification(
        bool isHighlighted,
        float expectedScale,
        float expectedTranslationY,
        int expectedDuration)
    {
        Assert.AreEqual(
            new DockItemMotionState(
                expectedScale,
                expectedTranslationY,
                expectedDuration),
            DockItemMotion.ResolveNeighbor(
                isHighlighted,
                reducedEffects: false));
    }

    [TestMethod]
    public void ReducedEffectsDisableNeighborMagnification()
    {
        Assert.AreEqual(
            new DockItemMotionState(1, 0, 0),
            DockItemMotion.ResolveNeighbor(
                isHighlighted: true,
                reducedEffects: true));
    }
}
