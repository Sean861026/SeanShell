using SeanShell.Core;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class ActivityIslandContentTests
{
    [TestMethod]
    public void ActivationPreservesFullWindowTitleForAccessibility()
    {
        var title = "繁體中文 / English " + new string('A', 500);
        var content = ActivityIslandContent.ForActivation("Application name", title);

        Assert.AreEqual(ActivityIslandContentKind.Activation, content.Kind);
        Assert.AreEqual("NOW ACTIVE", content.Label);
        Assert.AreEqual("\uE8A7", content.Glyph);
        Assert.AreEqual(title, content.Detail);
        StringAssert.Contains(content.AccessibleName, title);
    }

    [TestMethod]
    public void InputLayoutDoesNotClaimImeCompositionModeChanged()
    {
        var content = ActivityIslandContent.ForInputLayout("中文（繁體）— Microsoft 注音");

        Assert.AreEqual(ActivityIslandContentKind.InputLayout, content.Kind);
        Assert.AreEqual("KEYBOARD LAYOUT", content.Label);
        Assert.AreEqual("\uE765", content.Glyph);
        StringAssert.StartsWith(content.AccessibleName, "Keyboard layout:");
        StringAssert.Contains(content.AccessibleName, content.Title);
        StringAssert.Contains(content.Detail, "IME mode is separate");
    }

    [TestMethod]
    public void EnableConfirmationIsNotLabeledAsAppActivation()
    {
        var content = ActivityIslandContent.ForEnabledPreview();

        Assert.AreEqual(ActivityIslandContentKind.Preview, content.Kind);
        Assert.AreEqual("PREVIEW", content.Label);
        StringAssert.StartsWith(content.AccessibleName, "Activity Island preview:");
    }

    [TestMethod]
    public void EqualPayloadsHaveValueEqualityButKindsRemainDistinct()
    {
        var input = ActivityIslandContent.ForInputLayout("English");
        Assert.AreEqual(input, ActivityIslandContent.ForInputLayout("English"));
        Assert.AreNotEqual(input, ActivityIslandContent.ForActivation(input.Title, input.Detail));
    }

    [TestMethod]
    public void BlankProcessNameHasReadableFallback()
    {
        Assert.AreEqual("Application", ActivityIslandContent.ForActivation(" ", "Window").Title);
    }

    [TestMethod]
    public void InvalidSourceTextIsRejectedBeforePresentation()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => ActivityIslandContent.ForActivation(null!, "Window"));
        Assert.ThrowsExactly<ArgumentNullException>(() => ActivityIslandContent.ForActivation("App", null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => ActivityIslandContent.ForInputLayout(null!));
        Assert.ThrowsExactly<ArgumentException>(() => ActivityIslandContent.ForInputLayout(" "));
    }
}
