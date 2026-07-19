using SeanShell.Core;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class ShellStateStoreTests
{
    [TestMethod]
    public void SetMode_ChangesStateAndRaisesEvent()
    {
        var store = new ShellStateStore();
        ShellState? observed = null;
        store.StateChanged += (_, state) => observed = state;

        var changed = store.SetMode(ShellMode.Gaming);

        Assert.IsTrue(changed);
        Assert.AreEqual(ShellMode.Gaming, store.Current.Mode);
        Assert.AreSame(store.Current, observed);
    }

    [TestMethod]
    public void SetMode_WhenUnchanged_DoesNotRaiseEvent()
    {
        var store = new ShellStateStore();
        var eventCount = 0;
        store.StateChanged += (_, _) => eventCount++;

        var changed = store.SetMode(ShellMode.Normal);

        Assert.IsFalse(changed);
        Assert.AreEqual(0, eventCount);
    }
}
