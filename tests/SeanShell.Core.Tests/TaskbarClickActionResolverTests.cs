using SeanShell.Core;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class TaskbarClickActionResolverTests
{
    [DataRow(false, TaskbarClickAction.Default)]
    [DataRow(true, TaskbarClickAction.OpenNewInstance)]
    [TestMethod]
    public void ResolveMapsShiftToNewInstance(
        bool shiftPressed,
        TaskbarClickAction expected)
    {
        Assert.AreEqual(
            expected,
            TaskbarClickActionResolver.Resolve(shiftPressed));
    }
}
