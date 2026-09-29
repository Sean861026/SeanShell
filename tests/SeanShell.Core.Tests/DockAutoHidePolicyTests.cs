using SeanShell.Core;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class DockAutoHidePolicyTests
{
    [TestMethod]
    public void IdleDockCanCollapse()
    {
        Assert.IsTrue(DockAutoHidePolicy.CanCollapse(true, false, false, false, false));
    }

    [TestMethod]
    [DataRow(false, false, false, false, false)]
    [DataRow(true, true, false, false, false)]
    [DataRow(true, false, true, false, false)]
    [DataRow(true, false, false, true, false)]
    [DataRow(true, false, false, false, true)]
    [DataRow(true, true, true, true, true)]
    public void DisabledOrInteractiveDockStaysExpanded(
        bool enabled,
        bool pointerInside,
        bool hasKeyboardFocus,
        bool interactiveSurfaceOpen,
        bool gamingMode)
    {
        Assert.IsFalse(DockAutoHidePolicy.CanCollapse(
            enabled, pointerInside, hasKeyboardFocus, interactiveSurfaceOpen, gamingMode));
    }

    [TestMethod]
    public void OpeningSurfaceAfterTimerWasScheduledPreventsCollapse()
    {
        Assert.IsTrue(DockAutoHidePolicy.CanCollapse(true, false, false, false, false));
        Assert.IsFalse(DockAutoHidePolicy.CanCollapse(true, false, false, true, false));
        Assert.IsTrue(DockAutoHidePolicy.CanCollapse(true, false, false, false, false));
    }
}
