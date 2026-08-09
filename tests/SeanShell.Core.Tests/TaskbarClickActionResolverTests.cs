using SeanShell.Core;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class TaskbarClickActionResolverTests
{
    [DataRow(false, false, TaskbarClickAction.Default)]
    [DataRow(false, true, TaskbarClickAction.CycleWindows)]
    [DataRow(true, false, TaskbarClickAction.OpenNewInstance)]
    [DataRow(true, true, TaskbarClickAction.OpenElevatedInstance)]
    [TestMethod]
    public void ResolveMapsModifierState(
        bool shiftPressed,
        bool controlPressed,
        TaskbarClickAction expected)
    {
        Assert.AreEqual(
            expected,
            TaskbarClickActionResolver.Resolve(shiftPressed, controlPressed));
    }
}
