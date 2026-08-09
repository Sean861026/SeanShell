using SeanShell.Core;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class TaskbarWindowGroupActionResolverTests
{
    [TestMethod]
    public void EmptyGroupHasNoAction()
    {
        Assert.AreEqual(
            TaskbarWindowGroupAction.None,
            TaskbarWindowGroupActionResolver.Resolve([]));
    }

    [TestMethod]
    public void AnyVisibleWindowMinimizesTheGroup()
    {
        Assert.AreEqual(
            TaskbarWindowGroupAction.MinimizeAll,
            TaskbarWindowGroupActionResolver.Resolve([true, false, true]));
    }

    [TestMethod]
    public void EntirelyMinimizedGroupRestoresWithoutActivation()
    {
        Assert.AreEqual(
            TaskbarWindowGroupAction.RestoreAll,
            TaskbarWindowGroupActionResolver.Resolve([true, true]));
    }
}
