using SeanShell.Core;
using SeanShell.Gaming;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class GamingModeManagerTests
{
    [TestMethod]
    public void ReconcileEntersAndLeavesAutomaticGamingMode()
    {
        var stateStore = new ShellStateStore();
        var manager = new GamingModeManager(stateStore);
        manager.ConfigureAutomaticDetection(true, ["eldenring.exe"]);

        manager.Reconcile([Process(42, "ELDENRING")]);

        Assert.AreEqual(ShellMode.Gaming, stateStore.Current.Mode);
        Assert.AreEqual(1, manager.Current.ConfiguredRuleCount);
        CollectionAssert.AreEqual(new[] { "ELDENRING" }, manager.Current.ActiveGameNames.ToArray());

        manager.Reconcile([]);

        Assert.AreEqual(ShellMode.Normal, stateStore.Current.Mode);
        Assert.IsEmpty(manager.Current.ActiveGameNames);
    }

    [TestMethod]
    public void ManualModeKeepsGamingActiveAfterDetectedGameStops()
    {
        var stateStore = new ShellStateStore();
        var manager = new GamingModeManager(stateStore);
        manager.ConfigureAutomaticDetection(true, ["game"]);
        manager.Reconcile([Process(7, "game")]);
        manager.SetManualMode(true);

        manager.Reconcile([]);

        Assert.AreEqual(ShellMode.Gaming, stateStore.Current.Mode);
        Assert.IsTrue(manager.Current.ManualModeEnabled);

        manager.SetManualMode(false);

        Assert.AreEqual(ShellMode.Normal, stateStore.Current.Mode);
    }

    [TestMethod]
    public void DisablingAutomaticDetectionClearsDetectedGames()
    {
        var stateStore = new ShellStateStore();
        var manager = new GamingModeManager(stateStore);
        manager.ConfigureAutomaticDetection(true, ["game"]);
        manager.Reconcile([Process(7, "game")]);

        manager.ConfigureAutomaticDetection(false, ["game"]);

        Assert.AreEqual(ShellMode.Normal, stateStore.Current.Mode);
        Assert.IsFalse(manager.Current.AutomaticDetectionEnabled);
        Assert.IsEmpty(manager.Current.ActiveGameNames);
    }

    private static ProcessSnapshot Process(int id, string name) =>
        new(id, name, DateTimeOffset.UnixEpoch);
}
