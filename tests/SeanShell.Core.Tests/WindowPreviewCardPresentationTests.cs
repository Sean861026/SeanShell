using SeanShell.Core;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class WindowPreviewCardPresentationTests
{
    [TestMethod]
    public void ForegroundWindowUsesActiveAccentState()
    {
        Assert.AreEqual(
            new WindowPreviewCardVisualState(
                "Active",
                "Window is active.",
                true),
            WindowPreviewCardPresentation.Resolve(
                isMinimized: false,
                isForeground: true));
    }

    [TestMethod]
    public void BackgroundWindowUsesRunningNeutralState()
    {
        Assert.AreEqual(
            new WindowPreviewCardVisualState(
                "Running",
                "Window is running.",
                false),
            WindowPreviewCardPresentation.Resolve(
                isMinimized: false,
                isForeground: false));
    }

    [TestMethod]
    public void MinimizedStateTakesPriorityOverForegroundSnapshot()
    {
        Assert.AreEqual(
            new WindowPreviewCardVisualState(
                "Minimized",
                "Window is minimized.",
                false),
            WindowPreviewCardPresentation.Resolve(
                isMinimized: true,
                isForeground: true));
    }
}
