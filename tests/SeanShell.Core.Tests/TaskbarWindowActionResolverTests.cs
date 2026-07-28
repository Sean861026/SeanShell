using SeanShell.Core;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class TaskbarWindowActionResolverTests
{
    [TestMethod]
    public void ActiveVisibleWindowMinimizes()
    {
        var action = TaskbarWindowActionResolver.Resolve(
            isForeground: true,
            isMinimized: false);

        Assert.AreEqual(TaskbarWindowAction.Minimize, action);
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public void OtherWindowStatesRestoreAndActivate(
        bool isForeground,
        bool isMinimized)
    {
        var action = TaskbarWindowActionResolver.Resolve(
            isForeground,
            isMinimized);

        Assert.AreEqual(TaskbarWindowAction.RestoreAndActivate, action);
    }
}
