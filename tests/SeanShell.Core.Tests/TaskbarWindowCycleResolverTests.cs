using SeanShell.Core;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class TaskbarWindowCycleResolverTests
{
    [TestMethod]
    public void EmptyGroupHasNoTarget()
    {
        Assert.AreEqual(
            -1,
            TaskbarWindowCycleResolver.ResolveNextIndex([]));
    }

    [TestMethod]
    public void InactiveGroupStartsAtFirstWindow()
    {
        Assert.AreEqual(
            0,
            TaskbarWindowCycleResolver.ResolveNextIndex([false, false]));
    }

    [TestMethod]
    public void ActiveWindowAdvancesToNextWindow()
    {
        Assert.AreEqual(
            2,
            TaskbarWindowCycleResolver.ResolveNextIndex([false, true, false]));
    }

    [TestMethod]
    public void LastActiveWindowWrapsToFirstWindow()
    {
        Assert.AreEqual(
            0,
            TaskbarWindowCycleResolver.ResolveNextIndex([false, false, true]));
    }
}
