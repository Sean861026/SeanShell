using SeanShell.Core;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class DockShortcutTests
{
    [DataRow(DockShortcut.ControlAltD, "Ctrl + Alt + D")]
    [DataRow(DockShortcut.ControlShiftD, "Ctrl + Shift + D")]
    [TestMethod]
    public void GetDisplayNameReturnsReviewedChord(
        DockShortcut shortcut,
        string expected)
    {
        Assert.AreEqual(expected, shortcut.GetDisplayName());
    }
}
