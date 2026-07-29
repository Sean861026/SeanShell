using SeanShell.Core;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class AccessibilityLayoutTests
{
    [TestMethod]
    [DataRow(92, 1.0, 92)]
    [DataRow(92, 1.25, 102)]
    [DataRow(92, 1.5, 111)]
    [DataRow(92, 2.25, 138)]
    [DataRow(88, 2.25, 132)]
    public void ScaleDockHeightPreservesTextAtSupportedScaleFactors(
        int baseHeight,
        double textScaleFactor,
        int expected)
    {
        Assert.AreEqual(
            expected,
            AccessibilityLayout.ScaleDockHeight(baseHeight, textScaleFactor));
    }

    [TestMethod]
    public void ReducedEffectsFollowAnimationSetting()
    {
        Assert.IsFalse(
            new SystemAccessibilitySnapshot(true, 1, false).ReducedEffects);
        Assert.IsTrue(
            new SystemAccessibilitySnapshot(false, 1, false).ReducedEffects);
    }

    [TestMethod]
    public void HighContrastUsesSimplifiedEffects()
    {
        Assert.IsTrue(
            new SystemAccessibilitySnapshot(true, 1, true).ReducedEffects);
    }

    [TestMethod]
    [DataRow(256, 1.0, 256)]
    [DataRow(256, 1.5, 308)]
    [DataRow(256, 2.25, 384)]
    public void ScaleDockFixedControlsWidthPreservesScaledText(
        int baseWidth,
        double textScaleFactor,
        int expected)
    {
        Assert.AreEqual(
            expected,
            AccessibilityLayout.ScaleDockFixedControlsWidth(
                baseWidth,
                textScaleFactor));
    }
}
