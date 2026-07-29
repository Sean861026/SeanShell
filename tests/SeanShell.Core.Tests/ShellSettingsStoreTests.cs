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
        Assert.IsFalse(result.Settings.ReplaceWindowsTaskbar);
        Assert.AreEqual(string.Empty, result.Settings.PinnedApplicationIds);
        Assert.AreEqual(LauncherShortcut.AltSpace, result.Settings.LauncherShortcut);
        Assert.AreEqual(DockShortcut.ControlAltD, result.Settings.DockShortcut);
        Assert.AreEqual(ShellThemePreference.System, result.Settings.Theme);
        Assert.AreEqual(ShellDisplayDensity.Comfortable, result.Settings.DisplayDensity);
        Assert.AreEqual(string.Empty, result.Settings.DisabledPluginIds);
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
            ReplaceWindowsTaskbar = true,
            PinnedApplicationIds =
                "app:C:\\ProgramData\\Microsoft\\Windows\\Start Menu\\Programs\\Terminal.lnk",
            LauncherShortcut = LauncherShortcut.ControlAltSpace,
            DockShortcut = DockShortcut.ControlShiftD,
            Theme = ShellThemePreference.Dark,
            DisplayDensity = ShellDisplayDensity.Compact,
            AutomaticGamingModeEnabled = true,
            GameProcessRules = "eldenring",
            DisabledPluginIds = "seanshell.developer-tools",
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
    public void SaveNormalizesPinnedApplicationIds()
    {
        using var directory = new TemporaryDirectory();
        var store = new ShellSettingsStore(
            Path.Combine(directory.Path, "settings.json"));
        var ids = Enumerable
            .Range(1, PinnedApplicationIdList.MaximumCount + 2)
            .Select(index => $"app:C:\\App{index}.lnk")
            .Append("system:settings");

        store.Save(new ShellSettings
        {
            PinnedApplicationIds = string.Join('\n', ids),
        });
        var result = store.Load();

        var pinned = PinnedApplicationIdList.Parse(
            result.Settings.PinnedApplicationIds);
        Assert.HasCount(PinnedApplicationIdList.MaximumCount, pinned);
        Assert.IsFalse(result.Settings.PinnedApplicationIds.Contains(
            "system:settings",
            StringComparison.Ordinal));
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
        Assert.IsFalse(result.Settings.ReplaceWindowsTaskbar);
        Assert.AreEqual(string.Empty, result.Settings.GameProcessRules);
        Assert.AreEqual(string.Empty, result.Settings.DisabledPluginIds);
        Assert.AreEqual(ShellThemePreference.System, result.Settings.Theme);
        Assert.AreEqual(DockShortcut.ControlAltD, result.Settings.DockShortcut);
    }

    [TestMethod]
    public void LoadMigratesVersionTwoSettingsWithPluginsEnabledByDefault()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        File.WriteAllText(
            path,
            """
            {
              "schemaVersion": 2,
              "dockAutoHide": false,
              "launcherShortcut": "controlShiftSpace",
              "automaticGamingModeEnabled": true,
              "gameProcessRules": "notepad"
            }
            """);
        var store = new ShellSettingsStore(path);

        var result = store.Load();

        Assert.AreEqual(ShellSettings.CurrentSchemaVersion, result.Settings.SchemaVersion);
        Assert.IsTrue(result.Settings.AutomaticGamingModeEnabled);
        Assert.AreEqual("notepad", result.Settings.GameProcessRules);
        Assert.AreEqual(string.Empty, result.Settings.DisabledPluginIds);
        Assert.AreEqual(ShellThemePreference.System, result.Settings.Theme);
    }

    [TestMethod]
    public void LoadMigratesVersionThreeSettingsWithSystemThemeByDefault()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        File.WriteAllText(
            path,
            """
            {
              "schemaVersion": 3,
              "dockAutoHide": true,
              "launcherShortcut": "altSpace",
              "disabledPluginIds": "seanshell.wsl"
            }
            """);
        var store = new ShellSettingsStore(path);

        var result = store.Load();

        Assert.AreEqual(ShellSettings.CurrentSchemaVersion, result.Settings.SchemaVersion);
        Assert.AreEqual(ShellThemePreference.System, result.Settings.Theme);
        Assert.AreEqual("seanshell.wsl", result.Settings.DisabledPluginIds);
    }

    [TestMethod]
    public void LoadMigratesVersionFourSettingsWithComfortableDensityByDefault()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        File.WriteAllText(
            path,
            """
            {
              "schemaVersion": 4,
              "dockAutoHide": true,
              "launcherShortcut": "altSpace",
              "theme": "dark"
            }
            """);
        var store = new ShellSettingsStore(path);

        var result = store.Load();

        Assert.AreEqual(ShellSettings.CurrentSchemaVersion, result.Settings.SchemaVersion);
        Assert.AreEqual(ShellThemePreference.Dark, result.Settings.Theme);
        Assert.AreEqual(ShellDisplayDensity.Comfortable, result.Settings.DisplayDensity);
    }

    [TestMethod]
    public void LoadMigratesVersionFiveSettingsWithTaskbarReplacementOff()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        File.WriteAllText(
            path,
            """
            {
              "schemaVersion": 5,
              "dockAutoHide": true,
              "launcherShortcut": "altSpace",
              "theme": "dark",
              "displayDensity": "compact"
            }
            """);
        var store = new ShellSettingsStore(path);

        var result = store.Load();

        Assert.AreEqual(
            ShellSettings.CurrentSchemaVersion,
            result.Settings.SchemaVersion);
        Assert.IsFalse(result.Settings.ReplaceWindowsTaskbar);
        Assert.AreEqual(
            ShellDisplayDensity.Compact,
            result.Settings.DisplayDensity);
    }

    [TestMethod]
    public void LoadMigratesVersionSixSettingsWithNoPinnedApplications()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        File.WriteAllText(
            path,
            """
            {
              "schemaVersion": 6,
              "dockAutoHide": true,
              "replaceWindowsTaskbar": true,
              "launcherShortcut": "altSpace",
              "theme": "dark",
              "displayDensity": "compact"
            }
            """);
        var store = new ShellSettingsStore(path);

        var result = store.Load();

        Assert.AreEqual(
            ShellSettings.CurrentSchemaVersion,
            result.Settings.SchemaVersion);
        Assert.IsTrue(result.Settings.ReplaceWindowsTaskbar);
        Assert.AreEqual(string.Empty, result.Settings.PinnedApplicationIds);
    }

    [TestMethod]
    public void LoadMigratesVersionSevenSettingsWithDefaultDockShortcut()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        File.WriteAllText(
            path,
            """
            {
              "schemaVersion": 7,
              "dockAutoHide": true,
              "replaceWindowsTaskbar": true,
              "launcherShortcut": "controlAltSpace",
              "theme": "dark",
              "displayDensity": "compact",
              "pinnedApplicationIds": ""
            }
            """);
        var store = new ShellSettingsStore(path);

        var result = store.Load();

        Assert.AreEqual(
            ShellSettings.CurrentSchemaVersion,
            result.Settings.SchemaVersion);
        Assert.AreEqual(
            DockShortcut.ControlAltD,
            result.Settings.DockShortcut);
        Assert.AreEqual(
            LauncherShortcut.ControlAltSpace,
            result.Settings.LauncherShortcut);
    }

    [TestMethod]
    public void LoadRejectsUnsupportedDockShortcut()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        File.WriteAllText(
            path,
            $$"""
            {
              "schemaVersion": {{ShellSettings.CurrentSchemaVersion}},
              "dockShortcut": 99
            }
            """);
        var store = new ShellSettingsStore(path);

        var result = store.Load();

        Assert.AreEqual(new ShellSettings(), result.Settings);
        Assert.IsNotNull(result.Warning);
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
