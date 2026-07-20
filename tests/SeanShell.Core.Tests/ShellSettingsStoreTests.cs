using SeanShell.Core;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class ShellSettingsStoreTests
{
    [TestMethod]
    public void LoadReturnsDefaultsWhenSettingsDoNotExist()
    {
        using var directory = new TemporaryDirectory();
        var store = new ShellSettingsStore(Path.Combine(directory.Path, "settings.json"));

        var result = store.Load();

        Assert.IsTrue(result.Settings.DockAutoHide);
        Assert.AreEqual(LauncherShortcut.AltSpace, result.Settings.LauncherShortcut);
        Assert.IsFalse(result.WasRecovered);
        Assert.IsNull(result.Warning);
    }

    [TestMethod]
    public void SaveAndLoadRoundTripsSettings()
    {
        using var directory = new TemporaryDirectory();
        var store = new ShellSettingsStore(Path.Combine(directory.Path, "settings.json"));
        var expected = new ShellSettings
        {
            DockAutoHide = false,
            LauncherShortcut = LauncherShortcut.ControlAltSpace,
            AutomaticGamingModeEnabled = true,
            GameProcessRules = "eldenring",
        };

        store.Save(expected);
        var result = store.Load();

        Assert.AreEqual(expected, result.Settings);
        Assert.IsFalse(result.WasRecovered);
    }

    [TestMethod]
    public void FirstSaveCreatesARecoveryCopy()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        var store = new ShellSettingsStore(path);
        var expected = new ShellSettings { DockAutoHide = false };

        store.Save(expected);
        File.WriteAllText(path, "not-json");
        var result = store.Load();

        Assert.AreEqual(expected, result.Settings);
        Assert.IsTrue(result.WasRecovered);
    }

    [TestMethod]
    public void LoadRecoversLastKnownGoodSettingsWhenPrimaryIsDamaged()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        var store = new ShellSettingsStore(path);
        var lastKnownGood = new ShellSettings
        {
            DockAutoHide = false,
            LauncherShortcut = LauncherShortcut.AltSpace,
        };

        store.Save(lastKnownGood);
        store.Save(new ShellSettings { LauncherShortcut = LauncherShortcut.ControlShiftSpace });
        File.WriteAllText(path, "not-json");

        var result = store.Load();

        Assert.AreEqual(lastKnownGood, result.Settings);
        Assert.IsTrue(result.WasRecovered);
        Assert.IsNotNull(result.Warning);

        var repairedResult = store.Load();
        Assert.AreEqual(lastKnownGood, repairedResult.Settings);
        Assert.IsFalse(repairedResult.WasRecovered);
    }

    [TestMethod]
    public void LoadUsesSafeDefaultsWhenPrimaryAndBackupAreDamaged()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        File.WriteAllText(path, "not-json");
        File.WriteAllText($"{path}.bak", "also-not-json");
        var store = new ShellSettingsStore(path);

        var result = store.Load();

        Assert.AreEqual(new ShellSettings(), result.Settings);
        Assert.IsFalse(result.WasRecovered);
        Assert.IsNotNull(result.Warning);
    }

    [TestMethod]
    public void LoadMigratesVersionOneSettingsWithoutLosingPreferences()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        File.WriteAllText(
            path,
            """
            {
              "schemaVersion": 1,
              "dockAutoHide": false,
              "launcherShortcut": "controlAltSpace"
            }
            """);
        var store = new ShellSettingsStore(path);

        var result = store.Load();

        Assert.AreEqual(ShellSettings.CurrentSchemaVersion, result.Settings.SchemaVersion);
        Assert.IsFalse(result.Settings.DockAutoHide);
        Assert.AreEqual(LauncherShortcut.ControlAltSpace, result.Settings.LauncherShortcut);
        Assert.IsFalse(result.Settings.AutomaticGamingModeEnabled);
        Assert.AreEqual(string.Empty, result.Settings.GameProcessRules);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"SeanShell.Tests.{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
