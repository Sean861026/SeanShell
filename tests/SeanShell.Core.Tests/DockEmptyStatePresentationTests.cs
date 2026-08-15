using SeanShell.Core;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class DockEmptyStatePresentationTests
{
    [TestMethod]
    public void LoadingUsesCompactNonErrorPresentation()
    {
        var state = DockEmptyStatePresentation.Loading();

        Assert.AreEqual("\uE895", state.Glyph);
        Assert.AreEqual("Loading open windows", state.Description);
        Assert.IsFalse(state.IsError);
    }

    [TestMethod]
    public void NoWindowsIncludesNormalizedMonitorName()
    {
        var state = DockEmptyStatePresentation.NoWindows("  DISPLAY1\r\n");

        Assert.AreEqual("\uE8A7", state.Glyph);
        Assert.AreEqual(
            "No open application windows on DISPLAY1",
            state.Description);
        Assert.IsFalse(state.IsError);
    }

    [TestMethod]
    public void UnavailableKeepsReasonOutOfLayoutPresentation()
    {
        var state = DockEmptyStatePresentation.Unavailable(
            "An attempt was made\r\nto load a program with an incorrect format.");

        Assert.AreEqual("\uE783", state.Glyph);
        Assert.AreEqual(
            "Dock unavailable. An attempt was made to load a program with an incorrect format.",
            state.Description);
        Assert.IsTrue(state.IsError);
    }

    [TestMethod]
    public void UnavailableUsesHelpfulFallbackForBlankReason()
    {
        var state = DockEmptyStatePresentation.Unavailable("  ");

        Assert.AreEqual(
            "Dock unavailable. Window information is temporarily unavailable",
            state.Description);
    }
}
