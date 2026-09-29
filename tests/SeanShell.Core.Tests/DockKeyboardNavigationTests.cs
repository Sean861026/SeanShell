using SeanShell.Core;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class DockKeyboardNavigationTests
{
    [TestMethod]
    [DataRow(3, 0, DockNavigationDirection.Previous, 2)]
    [DataRow(3, 2, DockNavigationDirection.Next, 0)]
    [DataRow(3, 1, DockNavigationDirection.Previous, 0)]
    [DataRow(3, 1, DockNavigationDirection.Next, 2)]
    [DataRow(3, -1, DockNavigationDirection.Previous, 2)]
    [DataRow(3, -1, DockNavigationDirection.Next, 0)]
    [DataRow(3, 99, DockNavigationDirection.Next, 0)]
    [DataRow(3, 99, DockNavigationDirection.Previous, 2)]
    [DataRow(3, 1, DockNavigationDirection.First, 0)]
    [DataRow(3, 1, DockNavigationDirection.Last, 2)]
    [DataRow(1, 0, DockNavigationDirection.Next, 0)]
    [DataRow(1, 0, DockNavigationDirection.Previous, 0)]
    [DataRow(0, -1, DockNavigationDirection.Next, -1)]
    [DataRow(-1, -1, DockNavigationDirection.Previous, -1)]
    public void NavigationWrapsAndHandlesMissingSelection(
        int count, int selectedIndex, DockNavigationDirection direction, int expected)
    {
        Assert.AreEqual(expected, DockKeyboardNavigation.Move(count, selectedIndex, direction));
    }

    [TestMethod]
    public void InvalidDirectionDoesNotSelectAnItem()
    {
        Assert.AreEqual(-1, DockKeyboardNavigation.Move(3, 1, (DockNavigationDirection)99));
    }

    [TestMethod]
    public void LargeCountsDoNotOverflow()
    {
        Assert.AreEqual(int.MaxValue - 1,
            DockKeyboardNavigation.Move(int.MaxValue, 0, DockNavigationDirection.Previous));
        Assert.AreEqual(0,
            DockKeyboardNavigation.Move(int.MaxValue, int.MaxValue - 1, DockNavigationDirection.Next));
    }

    [TestMethod]
    public void RefreshPreservesKeyboardSelectedGroupAcrossReorderAndForegroundChange()
    {
        Assert.AreEqual(2, DockKeyboardNavigation.ResolveRefreshIndex(
            ["game", "browser", "editor"], "EDITOR", 0, true, 0));
    }

    [TestMethod]
    [DataRow(1, 1)]
    [DataRow(2, 1)]
    [DataRow(-1, 0)]
    public void ClosedGroupFallsBackToNearestRemainingSlot(int previous, int expected)
    {
        Assert.AreEqual(expected, DockKeyboardNavigation.ResolveRefreshIndex(
            ["game", "browser"], "closed-editor", previous, true, 0));
    }

    [TestMethod]
    [DataRow(1, 1)]
    [DataRow(-1, -1)]
    [DataRow(9, -1)]
    public void WithoutKeyboardFocusRefreshFollowsForegroundOnly(int foreground, int expected)
    {
        Assert.AreEqual(expected, DockKeyboardNavigation.ResolveRefreshIndex(
            ["game", "browser"], "browser", 1, false, foreground));
    }

    [TestMethod]
    public void EmptyRefreshDoesNotSelectAndNullKeysAreRejected()
    {
        Assert.AreEqual(-1, DockKeyboardNavigation.ResolveRefreshIndex([], "game", 0, true, 0));
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            DockKeyboardNavigation.ResolveRefreshIndex(null!, null, 0, true, 0));
    }
}
