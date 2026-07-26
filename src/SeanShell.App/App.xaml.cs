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
using SeanShell.Plugin.Docker;
using SeanShell.Plugin.DotNet;
using SeanShell.Plugin.Git;
using SeanShell.Plugin.Wsl;
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
    private static readonly TimeSpan StartupHealthyDelay = TimeSpan.FromSeconds(30);
    private readonly StartupCrashLoopGuard _startupGuard;
    private readonly CancellationTokenSource _startupHealthCancellation = new();
    private Guid? _startupSessionId;
    private Window? _window;

    public InstalledApplicationProvider InstalledApplications { get; } = new();

    public LauncherSearchService LauncherSearch { get; }

    public LauncherPerformanceMonitor LauncherPerformance { get; } = new();

    public PluginHost PluginHost { get; }

    public ExternalPluginCatalog ExternalPlugins { get; }

    public ShellSettingsStore SettingsStore { get; }

    public SettingsLoadResult SettingsLoad { get; }

    public StartupSessionResult? StartupSession { get; private set; }

    public ShellStateStore ShellState { get; } = new();

    public GamingModeManager GamingMode { get; }

    public GamingDetectionPerformanceMonitor GamingDetectionPerformance { get; } = new();

    public GamingSessionRecorder GamingSessions { get; }

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
        var startupHealthPath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SeanShell",
            "startup-health.json");
        var gamingSessionPath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SeanShell",
            "gaming-sessions.json");
        var externalPluginPath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SeanShell",
            "plugins");
        _startupGuard = new StartupCrashLoopGuard(startupHealthPath);
        SettingsStore = new ShellSettingsStore(settingsPath);
        SettingsLoad = SettingsStore.Load();
        RequestedTheme = SettingsLoad.Settings.Theme switch
        {
            ShellThemePreference.Light => ApplicationTheme.Light,
            ShellThemePreference.Dark => ApplicationTheme.Dark,
            _ => RequestedTheme,
        };
        GamingMode = new GamingModeManager(ShellState);
        GamingMode.ConfigureAutomaticDetection(
            SettingsLoad.Settings.AutomaticGamingModeEnabled,
            GameDetector.ParseRules(SettingsLoad.Settings.GameProcessRules));
        var gamingSessionStore = new GamingSessionStore(gamingSessionPath);
        GamingSessions = new GamingSessionRecorder(
            gamingSessionStore,
            gamingSessionStore.Load(),
            Environment.OSVersion.VersionString,
            typeof(App).Assembly.GetName().Version?.ToString() ?? "development");

        var developerWorkspaceRoots = GetDeveloperWorkspaceRoots();
        PluginHost = new PluginHost(
        [
            new PluginRegistration(DeveloperToolsPlugin.Manifest, new DeveloperToolsPlugin()),
            new PluginRegistration(DockerPlugin.Manifest, new DockerPlugin()),
            new PluginRegistration(
                DotNetWorkspacePlugin.Manifest,
                new DotNetWorkspacePlugin(developerWorkspaceRoots)),
            new PluginRegistration(GitPlugin.Manifest, new GitPlugin(developerWorkspaceRoots)),
            new PluginRegistration(WslPlugin.Manifest, new WslPlugin()),
        ],
        disabledPluginIds: PluginIdList.Parse(SettingsLoad.Settings.DisabledPluginIds));
        ExternalPlugins = new ExternalPluginCatalog(
            externalPluginPath,
            new WindowsAuthenticodeVerifier());

        LauncherSearch = new LauncherSearchService(
        [
            InstalledApplications,
            new SystemCommandProvider(),
            PluginHost,
        ]);

        InitializeComponent();
    }

    private static IReadOnlyList<string> GetDeveloperWorkspaceRoots()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var containingRepositories = new[]
        {
            GitRepositoryDiscovery.FindContainingRepository(Environment.CurrentDirectory),
            GitRepositoryDiscovery.FindContainingRepository(AppContext.BaseDirectory),
        };
        return containingRepositories
            .Where(static path => path is not null)
            .Cast<string>()
            .Concat(
            [
            System.IO.Path.Combine(userProfile, "source", "repos"),
            System.IO.Path.Combine(documents, "GitHub"),
            System.IO.Path.Combine(documents, "Repos"),
            System.IO.Path.Combine(documents, "Repositories"),
            ])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    /// <param name="args">Details about the launch request and process.</param>
    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        var automaticStartup = args.Arguments
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains("--startup", StringComparer.OrdinalIgnoreCase);
        var startup = _startupGuard.BeginSession(automaticStartup);
        StartupSession = startup;
        if (!startup.CanStart)
        {
            try
            {
                ExplorerRecoveryService.EnsureRunning();
            }
            catch
            {
                // The guard still exits rather than entering another automatic restart loop.
            }

            Exit();
            return;
        }

        if (startup.SessionId is not Guid startupSessionId)
        {
            Exit();
            return;
        }

        _startupSessionId = startupSessionId;
        _window = new MainWindow();
        _window.Closed += OnMainWindowClosed;
        _window.Activate();
        _ = WarmInstalledApplicationsAsync();
        _ = MarkStartupHealthyAsync(startupSessionId);
    }

    private async Task MarkStartupHealthyAsync(Guid sessionId)
    {
        try
        {
            await Task.Delay(
                StartupHealthyDelay,
                _startupHealthCancellation.Token).ConfigureAwait(false);
            _startupGuard.MarkHealthy(sessionId);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void OnMainWindowClosed(object sender, WindowEventArgs args)
    {
        _startupHealthCancellation.Cancel();
        if (_startupSessionId is Guid sessionId)
        {
            _startupGuard.MarkCleanExit(sessionId);
            _startupSessionId = null;
        }
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
