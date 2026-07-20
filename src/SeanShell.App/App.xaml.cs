using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using SeanShell.Core;
using SeanShell.Gaming;
using SeanShell.Plugin.DeveloperTools;
using SeanShell.PluginContracts;
using SeanShell.Plugins;
using SeanShell.Windows;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Foundation;
using Windows.Foundation.Collections;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace SeanShell.App;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    private Window? _window;

    public InstalledApplicationProvider InstalledApplications { get; } = new();

    public LauncherSearchService LauncherSearch { get; }

    public PluginHost PluginHost { get; }

    public ShellSettingsStore SettingsStore { get; }

    public SettingsLoadResult SettingsLoad { get; }

    public ShellStateStore ShellState { get; } = new();

    public GamingModeManager GamingMode { get; }

    public ProcessCatalog Processes { get; } = new();

    public DesktopWindowService DesktopWindows { get; } = new();

    public DisplayMonitorService Displays { get; } = new();

    public SystemMetricsProvider SystemMetrics { get; } = new();

    /// <summary>
    /// Initializes the singleton application object.  This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
        var settingsPath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SeanShell",
            "settings.json");
        SettingsStore = new ShellSettingsStore(settingsPath);
        SettingsLoad = SettingsStore.Load();
        GamingMode = new GamingModeManager(ShellState);
        GamingMode.ConfigureAutomaticDetection(
            SettingsLoad.Settings.AutomaticGamingModeEnabled,
            GameDetector.ParseRules(SettingsLoad.Settings.GameProcessRules));

        PluginHost = new PluginHost(
        [
            new PluginRegistration(DeveloperToolsPlugin.Manifest, new DeveloperToolsPlugin()),
        ]);

        LauncherSearch = new LauncherSearchService(
        [
            InstalledApplications,
            new SystemCommandProvider(),
            PluginHost,
        ]);

        InitializeComponent();
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    /// <param name="args">Details about the launch request and process.</param>
    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
        _ = WarmInstalledApplicationsAsync();
    }

    private async Task WarmInstalledApplicationsAsync()
    {
        try
        {
            await InstalledApplications.WarmAsync().ConfigureAwait(false);
        }
        catch
        {
            // The launcher remains usable with built-in system commands if indexing fails.
        }
    }
}
