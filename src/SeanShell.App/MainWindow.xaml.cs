using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using SeanShell.Core;
using SeanShell.Gaming;
using SeanShell.Plugins;
using SeanShell.Windows;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace SeanShell.App;

/// <summary>
/// The application window. This hosts a Frame that displays pages. Add your
/// UI and logic to MainPage.xaml / MainPage.xaml.cs instead of here so you
/// can use Page features such as navigation events and the Loaded lifecycle.
/// </summary>
public sealed partial class MainWindow : Window
{
    private const uint SpaceVirtualKey = 0x20;
    private static readonly TimeSpan GamingDetectionInterval = TimeSpan.FromSeconds(2);
    private readonly DesktopWindowService _desktopWindows;
    private readonly DisplayMonitorService _displayMonitorService;
    private readonly DispatcherQueueTimer _displayChangeTimer;
    private readonly DispatcherQueueTimer _dockRefreshTimer;
    private readonly DispatcherQueueTimer _clockRefreshTimer;
    private readonly DispatcherQueueTimer _taskbarRefreshTimer;
    private readonly InstalledApplicationProvider _installedApplications;
    private readonly LauncherWindow _launcherWindow;
    private readonly GamingModeManager _gamingMode;
    private readonly GamingDetectionPerformanceMonitor _gamingDetectionPerformance;
    private readonly GamingSessionRecorder _gamingSessions;
    private readonly DispatcherQueueTimer _gamingModeTimer;
    private readonly ProcessCatalog _processCatalog;
    private readonly PluginHost _pluginHost;
    private readonly ExternalPluginTrustManager _externalPluginTrust;
    private readonly ShellStateStore _shellState;
    private readonly ShellSettingsStore _settingsStore;
    private readonly TaskbarReplacementSession _taskbarReplacement;
    private SystemAccessibilityService? _accessibility;
    private SystemAccessibilitySnapshot _systemAccessibility = new(true, 1);
    private DisplayChangeObserver? _displayChangeObserver;
    private IReadOnlyList<DisplayMonitorSnapshot> _monitors;
    private IReadOnlyList<DockWindow> _dockWindows;
    private IReadOnlyList<ShellCommand> _availableApplications = [];
    private IReadOnlyList<ShellCommand> _pinnedApplications = [];
    private bool _refreshingDockWindows;
    private bool _refreshingGamingMode;
    private bool _taskbarAccessRevealed;
    private GlobalHotKey? _launcherHotKey;
    private LauncherShortcut? _registeredShortcut;
    private ShellSettings _settings;

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        AppWindow.SetIcon("Assets/AppIcon.ico");

        // Navigate the root frame to the main page on startup.
        RootFrame.Navigate(typeof(MainPage));

        var app = (App)Application.Current;
        _settingsStore = app.SettingsStore;
        _settings = app.SettingsLoad.Settings;
        _gamingMode = app.GamingMode;
        _gamingDetectionPerformance = app.GamingDetectionPerformance;
        _gamingSessions = app.GamingSessions;
        _pluginHost = app.PluginHost;
        _externalPluginTrust = app.ExternalPluginTrust;
        _processCatalog = app.Processes;
        _desktopWindows = app.DesktopWindows;
        _displayMonitorService = app.Displays;
        _installedApplications = app.InstalledApplications;
        _shellState = app.ShellState;
        _launcherWindow = new LauncherWindow(app.LauncherSearch, app.LauncherPerformance);
        _launcherWindow.PinChangedRequested += OnPinnedApplicationChangedAsync;
        _launcherWindow.SetPinnedApplicationIds(
            PinnedApplicationIdList.Parse(_settings.PinnedApplicationIds));
        _taskbarReplacement = new TaskbarReplacementSession(
            new WindowsTaskbarController(),
            new TaskbarRecoveryGuard(
                Environment.ProcessPath ??
                throw new InvalidOperationException(
                    "The packaged SeanShell executable path is unavailable."),
                Environment.ProcessId));
        _monitors = _displayMonitorService.Capture();
        _dockWindows = CreateDockWindows(_monitors);

        if (RootFrame.Content is MainPage mainPage)
        {
            mainPage.LauncherRequested += OnLauncherRequested;
            mainPage.DockAutoHideChanged += OnDockAutoHideChanged;
            mainPage.TaskbarReplacementChanged += OnTaskbarReplacementChanged;
            mainPage.LauncherShortcutChanged += OnLauncherShortcutChanged;
            mainPage.ThemePreferenceChanged += OnThemePreferenceChanged;
            mainPage.DisplayDensityChanged += OnDisplayDensityChanged;
            mainPage.AutomaticGamingModeChanged += OnAutomaticGamingModeChanged;
            mainPage.GameProcessRulesSaved += OnGameProcessRulesSaved;
            mainPage.ManualGamingModeChanged += OnManualGamingModeChanged;
            mainPage.PluginEnabledChanged += OnPluginEnabledChanged;
            mainPage.ExternalPluginConsentChanged += OnExternalPluginConsentChanged;
            mainPage.ExternalPluginTrustClearRequested += OnExternalPluginTrustClearRequested;
        }

        _gamingModeTimer = DispatcherQueue.CreateTimer();
        _gamingModeTimer.Interval = GamingDetectionInterval;
        _gamingModeTimer.Tick += OnGamingModeTimerTick;

        _dockRefreshTimer = DispatcherQueue.CreateTimer();
        _dockRefreshTimer.Interval = TimeSpan.FromSeconds(2);
        _dockRefreshTimer.Tick += OnDockRefreshTimerTick;

        _clockRefreshTimer = DispatcherQueue.CreateTimer();
        _clockRefreshTimer.Interval = TimeSpan.FromSeconds(15);
        _clockRefreshTimer.Tick += OnClockRefreshTimerTick;

        _taskbarRefreshTimer = DispatcherQueue.CreateTimer();
        _taskbarRefreshTimer.Interval = TimeSpan.FromSeconds(2);
        _taskbarRefreshTimer.Tick += OnTaskbarRefreshTimerTick;

        _displayChangeTimer = DispatcherQueue.CreateTimer();
        _displayChangeTimer.Interval = TimeSpan.FromMilliseconds(500);
        _displayChangeTimer.IsRepeating = false;
        _displayChangeTimer.Tick += OnDisplayChangeTimerTick;
        TryObserveDisplayChanges();

        RegisterLauncherHotKey(_settings.LauncherShortcut);
        _shellState.StateChanged += OnShellStateChanged;
        _gamingMode.StatusChanged += OnGamingSessionStatusChanged;
        Activated += OnActivated;
        Closed += OnClosed;
    }

    private async void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        Activated -= OnActivated;
        _accessibility = new SystemAccessibilityService();
        _accessibility.Changed += OnAccessibilityChanged;
        _systemAccessibility = _accessibility.Current;
        ApplySystemAccessibility(_systemAccessibility);
        foreach (var dockWindow in _dockWindows)
        {
            dockWindow.ShowDock();
            dockWindow.SetAutoHide(_settings.DockAutoHide);
        }

        ApplyTaskbarReplacementOnStartup();
        RefreshClock();
        _clockRefreshTimer.Start();
        _dockRefreshTimer.Start();
        _ = RefreshDockWindowsAsync();
        _ = RefreshAvailableApplicationsAsync();
        _ = RefreshPinnedApplicationsAsync();
        UpdateGamingModeMonitor();
        await _pluginHost.InitializeAsync().ConfigureAwait(true);
        if (_gamingMode.Current.IsGaming)
        {
            await _pluginHost.SuspendAsync().ConfigureAwait(true);
        }
    }

    private async void OnShellStateChanged(object? sender, ShellState state)
    {
        try
        {
            if (state.Mode == ShellMode.Gaming)
            {
                PrepareTaskbarReplacementForGaming();
                _dockRefreshTimer.Stop();
                UpdateReducedEffects();
                await _pluginHost.SuspendAsync().ConfigureAwait(true);
            }
            else
            {
                if (ShouldReserveDockWorkArea())
                {
                    var reservation = SetDockWorkAreaReservation(true);
                    if (!reservation.Success)
                    {
                        FailTaskbarReplacement(
                            reservation.Error ??
                            "Windows did not reserve the Dock work area.");
                    }
                }

                UpdateReducedEffects();
                _dockRefreshTimer.Start();
                _ = RefreshDockWindowsAsync();
                await _pluginHost.ResumeAsync().ConfigureAwait(true);
            }
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void OnManualGamingModeChanged(bool enabled)
    {
        _gamingMode.SetManualMode(enabled);
    }

    private void OnGamingSessionStatusChanged(object? sender, GamingModeStatus status)
    {
        var transition = _gamingSessions.Observe(
            status,
            _gamingDetectionPerformance.Current,
            DateTimeOffset.UtcNow);
        if (transition == GamingSessionTransition.Started)
        {
            _gamingDetectionPerformance.Reset();
        }
    }

    private async void OnPluginEnabledChanged(string pluginId, bool enabled)
    {
        var previousSettings = _settings;
        try
        {
            var result = await _pluginHost.SetEnabledAsync(pluginId, enabled).ConfigureAwait(true);
            if (!result.Success)
            {
                if (RootFrame.Content is MainPage failedPage)
                {
                    failedPage.SetPluginEnabledFailed(
                        pluginId,
                        result.Diagnostic.LastError ?? "The plugin rejected the lifecycle change.");
                }

                return;
            }

            var disabledIds = PluginIdList.Parse(_settings.DisabledPluginIds)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (enabled)
            {
                disabledIds.Remove(pluginId);
            }
            else
            {
                disabledIds.Add(pluginId);
            }

            _settings = _settings with
            {
                DisabledPluginIds = PluginIdList.Serialize(disabledIds),
            };
            if (!PersistSettings())
            {
                _settings = previousSettings;
                var rollback = await _pluginHost.SetEnabledAsync(pluginId, !enabled).ConfigureAwait(true);
                if (RootFrame.Content is MainPage rollbackPage)
                {
                    rollbackPage.SetPluginEnabledFailed(
                        pluginId,
                        rollback.Success
                            ? "The settings file could not be updated, so the previous plugin state was restored."
                            : $"The settings file could not be updated and the previous state could not be restored. {rollback.Diagnostic.LastError}");
                }

                return;
            }

            if (RootFrame.Content is MainPage mainPage)
            {
                mainPage.SetPluginEnabledApplied(
                    pluginId,
                    result.Diagnostic.Name,
                    enabled);
            }
        }
        catch (Exception exception)
        {
            _settings = previousSettings;
            if (RootFrame.Content is MainPage mainPage)
            {
                mainPage.SetPluginEnabledFailed(pluginId, exception.Message);
            }
        }
    }

    private void OnExternalPluginConsentChanged(
        ExternalPluginCandidate candidate,
        bool approved)
    {
        try
        {
            if (approved)
            {
                _externalPluginTrust.Approve(candidate, DateTimeOffset.UtcNow);
            }
            else
            {
                _externalPluginTrust.Revoke(candidate);
            }

            if (RootFrame.Content is MainPage mainPage)
            {
                mainPage.SetExternalPluginConsentApplied(candidate, approved);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
        {
            if (RootFrame.Content is MainPage mainPage)
            {
                mainPage.SetExternalPluginConsentFailed(candidate, exception.Message);
            }
        }
    }

    private void OnExternalPluginTrustClearRequested()
    {
        try
        {
            _externalPluginTrust.RevokeAll();
            if (RootFrame.Content is MainPage mainPage)
            {
                mainPage.SetExternalPluginTrustCleared();
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            if (RootFrame.Content is MainPage mainPage)
            {
                mainPage.SetExternalPluginTrustClearFailed(exception.Message);
            }
        }
    }

    private void OnAutomaticGamingModeChanged(bool enabled)
    {
        _gamingDetectionPerformance.Reset();
        _settings = _settings with { AutomaticGamingModeEnabled = enabled };
        _gamingMode.ConfigureAutomaticDetection(
            enabled,
            GameDetector.ParseRules(_settings.GameProcessRules));
        if (PersistSettings() && RootFrame.Content is MainPage mainPage)
        {
            mainPage.SetGamingSettingsApplied(
                "Automatic detection updated",
                enabled ? "SeanShell is watching the configured game process names." : "Automatic game detection is off.");
        }

        UpdateGamingModeMonitor();
    }

    private void OnGameProcessRulesSaved(string rules)
    {
        _gamingDetectionPerformance.Reset();
        var processNames = GameDetector.ParseRules(rules);
        var normalizedRules = string.Join(Environment.NewLine, processNames);
        _settings = _settings with { GameProcessRules = normalizedRules };
        _gamingMode.ConfigureAutomaticDetection(
            _settings.AutomaticGamingModeEnabled,
            processNames);
        var persisted = PersistSettings();
        if (RootFrame.Content is MainPage mainPage)
        {
            mainPage.SetGameProcessRulesApplied(normalizedRules, processNames.Count, persisted);
        }

        UpdateGamingModeMonitor();
    }

    private void OnDockAutoHideChanged(bool enabled)
    {
        foreach (var dockWindow in _dockWindows)
        {
            dockWindow.SetAutoHide(enabled);
        }

        _settings = _settings with { DockAutoHide = enabled };
        PersistSettings();
    }

    private void OnTaskbarReplacementChanged(bool enabled)
    {
        var previousSettings = _settings;
        _settings = _settings with { ReplaceWindowsTaskbar = enabled };
        if (!PersistSettings())
        {
            _settings = previousSettings;
            if (RootFrame.Content is MainPage saveFailedPage)
            {
                saveFailedPage.SetTaskbarReplacementPreferenceUnchanged(
                    previousSettings.ReplaceWindowsTaskbar,
                    "The preference could not be saved, so the current taskbar state remains active.");
            }

            return;
        }

        WorkAreaReservationResult? release = null;
        if (!enabled)
        {
            release = SetDockWorkAreaReservation(false);
        }

        var result = enabled
            ? _taskbarReplacement.Enable()
            : _taskbarReplacement.Disable();
        if (result.Success)
        {
            _taskbarAccessRevealed = false;
            if (enabled)
            {
                var reservation = ShouldReserveDockWorkArea()
                    ? SetDockWorkAreaReservation(true)
                    : WorkAreaReservationResult.Released();
                if (!reservation.Success)
                {
                    FailTaskbarReplacement(
                        reservation.Error ??
                        "Windows did not reserve the Dock work area.");
                    return;
                }

                _taskbarRefreshTimer.Start();
            }
            else
            {
                _taskbarRefreshTimer.Stop();
            }

            UpdateDockSystemAreaState();
            if (RootFrame.Content is MainPage appliedPage)
            {
                if (release is { Success: false })
                {
                    appliedPage.SetTaskbarReplacementFailed(
                        release.Error ??
                        "Windows did not release the Dock work area.");
                    return;
                }

                appliedPage.SetTaskbarReplacementApplied(
                    enabled,
                    result.TaskbarCount);
            }

            return;
        }

        _taskbarRefreshTimer.Stop();
        _taskbarAccessRevealed = false;
        UpdateDockSystemAreaState();
        _settings = _settings with { ReplaceWindowsTaskbar = false };
        _ = PersistSettings();
        if (RootFrame.Content is MainPage failedPage)
        {
            failedPage.SetTaskbarReplacementFailed(
                result.Error ?? "Windows did not change taskbar visibility.");
        }
    }

    private void ApplyTaskbarReplacementOnStartup()
    {
        if (!_settings.ReplaceWindowsTaskbar)
        {
            return;
        }

        var result = _taskbarReplacement.Enable();
        if (result.Success)
        {
            _taskbarAccessRevealed = false;
            var reservation = ShouldReserveDockWorkArea()
                ? SetDockWorkAreaReservation(true)
                : WorkAreaReservationResult.Released();
            if (!reservation.Success)
            {
                FailTaskbarReplacement(
                    reservation.Error ??
                    "Windows did not reserve the Dock work area.");
                return;
            }

            _taskbarRefreshTimer.Start();
            UpdateDockSystemAreaState();
            if (RootFrame.Content is MainPage mainPage)
            {
                mainPage.SetTaskbarReplacementApplied(
                    true,
                    result.TaskbarCount);
            }

            return;
        }

        _settings = _settings with { ReplaceWindowsTaskbar = false };
        _taskbarAccessRevealed = false;
        UpdateDockSystemAreaState();
        _ = PersistSettings();
        if (RootFrame.Content is MainPage failedPage)
        {
            failedPage.SetTaskbarReplacementFailed(
                result.Error ?? "Windows did not change taskbar visibility.");
        }
    }

    private void OnThemePreferenceChanged(ShellThemePreference theme)
    {
        var previousSettings = _settings;
        _settings = _settings with { Theme = theme };
        var persisted = PersistSettings();
        if (!persisted)
        {
            _settings = previousSettings;
        }

        if (RootFrame.Content is MainPage mainPage)
        {
            mainPage.SetThemePreferenceApplied(persisted ? theme : previousSettings.Theme, persisted);
        }
    }

    private void OnDisplayDensityChanged(ShellDisplayDensity density)
    {
        var previousSettings = _settings;
        _settings = _settings with { DisplayDensity = density };
        var persisted = PersistSettings();
        if (!persisted)
        {
            _settings = previousSettings;
        }

        if (RootFrame.Content is MainPage mainPage)
        {
            mainPage.SetDisplayDensityApplied(
                persisted ? density : previousSettings.DisplayDensity,
                persisted);
        }
    }

    private void OnAccessibilityChanged(object? sender, SystemAccessibilitySnapshot snapshot)
    {
        DispatcherQueue.TryEnqueue(() => ApplySystemAccessibility(snapshot));
    }

    private void ApplySystemAccessibility(SystemAccessibilitySnapshot snapshot)
    {
        _systemAccessibility = snapshot;
        foreach (var dockWindow in _dockWindows)
        {
            dockWindow.ApplyTextScaleFactor(snapshot.TextScaleFactor);
        }

        if (ShouldReserveDockWorkArea())
        {
            var reservation = SetDockWorkAreaReservation(true);
            if (!reservation.Success)
            {
                FailTaskbarReplacement(
                    reservation.Error ??
                    "Windows did not resize the Dock work area.");
            }
        }

        UpdateReducedEffects();
    }

    private void UpdateReducedEffects()
    {
        var enabled = _shellState.Current.Mode == ShellMode.Gaming ||
            _systemAccessibility.ReducedEffects;
        SystemBackdrop = enabled ? null : new MicaBackdrop();
        WindowRoot.Background = enabled
            ? Application.Current.Resources["ApplicationPageBackgroundThemeBrush"] as Brush
            : null;
        _launcherWindow.SetReducedEffects(enabled);
    }

    private void TryObserveDisplayChanges()
    {
        try
        {
            var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
            _displayChangeObserver = new DisplayChangeObserver(windowHandle);
            _displayChangeObserver.Changed += OnDisplaysChanged;
        }
        catch (Exception exception)
        {
            if (RootFrame.Content is MainPage mainPage)
            {
                mainPage.SetDisplayMonitoringUnavailable(exception.Message);
            }
        }
    }

    private void OnDisplaysChanged(object? sender, EventArgs e)
    {
        _displayChangeTimer.Stop();
        _displayChangeTimer.Start();
    }

    private void OnDisplayChangeTimerTick(DispatcherQueueTimer sender, object args)
    {
        _displayChangeTimer.Stop();
        RebuildDockWindows();
    }

    private void RebuildDockWindows()
    {
        try
        {
            var monitors = _displayMonitorService.Capture();
            if (monitors.Count == 0 ||
                DisplayTopologyComparer.AreEquivalent(_monitors, monitors))
            {
                return;
            }

            var replacements = CreateDockWindows(monitors);
            foreach (var dockWindow in replacements)
            {
                dockWindow.SetAutoHide(_settings.DockAutoHide);
            }

            var previous = _dockWindows;
            _dockWindows = replacements;
            _monitors = monitors;
            foreach (var dockWindow in previous)
            {
                dockWindow.PinChangedRequested -= OnPinnedApplicationChangedAsync;
                dockWindow.PinMoveRequested -= OnPinnedApplicationMovedAsync;
                dockWindow.Shutdown();
            }

            if (ShouldReserveDockWorkArea())
            {
                var reservation = SetDockWorkAreaReservation(true);
                if (!reservation.Success)
                {
                    FailTaskbarReplacement(
                        reservation.Error ??
                        "Windows did not reserve the Dock work area.");
                }
            }

            if (!_gamingMode.Current.IsGaming)
            {
                foreach (var dockWindow in _dockWindows)
                {
                    dockWindow.ShowDock();
                }

                _ = RefreshDockWindowsAsync();
            }

            if (RootFrame.Content is MainPage mainPage)
            {
                mainPage.SetDisplayCount(_monitors.Count);
            }
        }
        catch (Exception exception)
        {
            if (RootFrame.Content is MainPage mainPage)
            {
                mainPage.SetDisplayMonitoringUnavailable(exception.Message);
            }
        }
    }

    private IReadOnlyList<DockWindow> CreateDockWindows(
        IReadOnlyList<DisplayMonitorSnapshot> monitors)
    {
        var windows = new List<DockWindow>(monitors.Count);
        try
        {
            foreach (var monitor in monitors)
            {
                var dockWindow = new DockWindow(
                    _desktopWindows,
                    _shellState,
                    monitor,
                    _systemAccessibility.TextScaleFactor);
                dockWindow.LauncherRequested += OnLauncherRequested;
                dockWindow.SystemAreaRequested += OnSystemAreaRequested;
                dockWindow.PinChangedRequested += OnPinnedApplicationChangedAsync;
                dockWindow.PinMoveRequested += OnPinnedApplicationMovedAsync;
                dockWindow.ApplyAvailableApplications(_availableApplications);
                dockWindow.ApplyPinnedApplications(_pinnedApplications);
                dockWindow.ApplyClock(DateTimeOffset.Now);
                dockWindow.SetSystemAreaAccessState(
                    _settings.ReplaceWindowsTaskbar &&
                    _taskbarReplacement.IsEnabled,
                    _taskbarAccessRevealed);
                windows.Add(dockWindow);
            }

            return windows;
        }
        catch
        {
            foreach (var window in windows)
            {
                window.Shutdown();
            }

            throw;
        }
    }

    private async void OnDockRefreshTimerTick(DispatcherQueueTimer sender, object args)
    {
        await RefreshDockWindowsAsync().ConfigureAwait(true);
    }

    private void OnClockRefreshTimerTick(
        DispatcherQueueTimer sender,
        object args) =>
        RefreshClock();

    private void RefreshClock()
    {
        var timestamp = DateTimeOffset.Now;
        foreach (var dockWindow in _dockWindows)
        {
            dockWindow.ApplyClock(timestamp);
        }
    }

    private void OnTaskbarRefreshTimerTick(
        DispatcherQueueTimer sender,
        object args)
    {
        var result = _taskbarReplacement.EnsureHidden();
        if (result.Success)
        {
            return;
        }

        FailTaskbarReplacement(
            result.Error ??
            "Windows did not keep the native taskbars hidden.");
    }

    private void OnSystemAreaRequested(object? sender, EventArgs e)
    {
        if (!_settings.ReplaceWindowsTaskbar ||
            !_taskbarReplacement.IsEnabled)
        {
            UpdateDockSystemAreaState();
            return;
        }

        var result = _taskbarAccessRevealed
            ? _taskbarReplacement.EnsureHidden()
            : RevealTaskbarSystemArea();
        if (!result.Success)
        {
            FailTaskbarReplacement(
                result.Error ??
                "Windows did not change taskbar visibility.");
            return;
        }

        _taskbarAccessRevealed = !_taskbarAccessRevealed;
        if (!_taskbarAccessRevealed)
        {
            var reservation = SetDockWorkAreaReservation(true);
            if (!reservation.Success)
            {
                FailTaskbarReplacement(
                    reservation.Error ??
                    "Windows did not restore the Dock work area.");
                return;
            }
        }

        if (_taskbarAccessRevealed)
        {
            _taskbarRefreshTimer.Stop();
        }
        else
        {
            _taskbarRefreshTimer.Start();
        }

        UpdateDockSystemAreaState();
        if (RootFrame.Content is MainPage mainPage)
        {
            mainPage.SetSystemAreaAccessApplied(
                _taskbarAccessRevealed,
                result.TaskbarCount);
        }
    }

    private TaskbarOperationResult RevealTaskbarSystemArea()
    {
        var release = SetDockWorkAreaReservation(false);
        if (!release.Success)
        {
            return new TaskbarOperationResult(
                false,
                0,
                release.Error ?? "Windows did not release the Dock work area.");
        }

        return _taskbarReplacement.Reveal();
    }

    private void PrepareTaskbarReplacementForGaming()
    {
        if (!_settings.ReplaceWindowsTaskbar ||
            !_taskbarReplacement.IsEnabled)
        {
            return;
        }

        if (_taskbarAccessRevealed)
        {
            var result = _taskbarReplacement.EnsureHidden();
            if (!result.Success)
            {
                FailTaskbarReplacement(
                    result.Error ??
                    "Windows did not resume taskbar replacement.");
                return;
            }
        }

        _taskbarAccessRevealed = false;
        var release = SetDockWorkAreaReservation(false);
        if (!release.Success)
        {
            FailTaskbarReplacement(
                release.Error ??
                "Windows did not release the Dock work area.");
            return;
        }

        _taskbarRefreshTimer.Start();
        UpdateDockSystemAreaState();
    }

    private void FailTaskbarReplacement(string message)
    {
        _taskbarRefreshTimer.Stop();
        _taskbarAccessRevealed = false;
        _settings = _settings with { ReplaceWindowsTaskbar = false };
        _ = PersistSettings();
        _ = SetDockWorkAreaReservation(false);
        _ = _taskbarReplacement.Disable();
        UpdateDockSystemAreaState();
        if (RootFrame.Content is MainPage mainPage)
        {
            mainPage.SetTaskbarReplacementFailed(message);
        }
    }

    private void UpdateDockSystemAreaState()
    {
        var available =
            _settings.ReplaceWindowsTaskbar &&
            _taskbarReplacement.IsEnabled;
        foreach (var dockWindow in _dockWindows)
        {
            dockWindow.SetSystemAreaAccessState(
                available,
                _taskbarAccessRevealed);
        }
    }

    private bool ShouldReserveDockWorkArea() =>
        _settings.ReplaceWindowsTaskbar &&
        _taskbarReplacement.IsEnabled &&
        !_taskbarAccessRevealed &&
        !_gamingMode.Current.IsGaming;

    private WorkAreaReservationResult SetDockWorkAreaReservation(bool enabled)
    {
        WorkAreaReservationResult result = WorkAreaReservationResult.Released();
        foreach (var dockWindow in _dockWindows)
        {
            var current = dockWindow.SetWorkAreaReservation(enabled);
            if (!current.Success && result.Success)
            {
                result = current;
            }
        }

        if (enabled && !result.Success)
        {
            foreach (var dockWindow in _dockWindows)
            {
                _ = dockWindow.SetWorkAreaReservation(false);
            }
        }

        return result;
    }

    private async Task RefreshDockWindowsAsync()
    {
        if (_refreshingDockWindows || _gamingMode.Current.IsGaming)
        {
            return;
        }

        _refreshingDockWindows = true;
        try
        {
            var snapshot = await Task.Run(_desktopWindows.Capture).ConfigureAwait(true);
            foreach (var dockWindow in _dockWindows)
            {
                dockWindow.ApplyWindowSnapshot(snapshot);
            }
        }
        catch (Exception exception)
        {
            foreach (var dockWindow in _dockWindows)
            {
                dockWindow.SetWindowSnapshotUnavailable(exception.Message);
            }
        }
        finally
        {
            _refreshingDockWindows = false;
        }
    }

    private async Task<bool> OnPinnedApplicationChangedAsync(
        ShellCommand command,
        bool shouldPin)
    {
        if (command.Kind != ShellCommandKind.Application)
        {
            throw new InvalidOperationException(
                "Only installed applications can be pinned to the Dock.");
        }

        var available = await _installedApplications
            .GetByIdsAsync([command.Id])
            .ConfigureAwait(true);
        if (available.Count != 1)
        {
            throw new InvalidOperationException(
                "This Start Menu shortcut is no longer available.");
        }

        var previousSettings = _settings;
        var applicationIds = PinnedApplicationIdList
            .Parse(_settings.PinnedApplicationIds)
            .ToList();
        var existingIndex = applicationIds.FindIndex(
            id => string.Equals(
                id,
                command.Id,
                StringComparison.OrdinalIgnoreCase));
        if (shouldPin)
        {
            if (existingIndex >= 0)
            {
                return true;
            }

            if (applicationIds.Count >= PinnedApplicationIdList.MaximumCount)
            {
                throw new InvalidOperationException(
                    $"The Dock supports up to {PinnedApplicationIdList.MaximumCount} pinned applications.");
            }

            applicationIds.Add(command.Id);
        }
        else if (existingIndex >= 0)
        {
            applicationIds.RemoveAt(existingIndex);
        }

        _settings = _settings with
        {
            PinnedApplicationIds =
                PinnedApplicationIdList.Serialize(applicationIds),
        };
        if (!PersistSettings())
        {
            _settings = previousSettings;
            throw new IOException(
                "The pinned application preference could not be saved.");
        }

        _launcherWindow.SetPinnedApplicationIds(applicationIds);
        await RefreshPinnedApplicationsAsync().ConfigureAwait(true);
        if (RootFrame.Content is MainPage mainPage)
        {
            mainPage.SetPinnedApplicationsApplied(
                command.Title,
                shouldPin,
                _pinnedApplications.Count);
        }

        return true;
    }

    private async Task RefreshPinnedApplicationsAsync()
    {
        try
        {
            var applicationIds =
                PinnedApplicationIdList.Parse(_settings.PinnedApplicationIds);
            var applications = await _installedApplications
                .GetByIdsAsync(applicationIds)
                .ConfigureAwait(true);
            _pinnedApplications = applications;
            foreach (var dockWindow in _dockWindows)
            {
                dockWindow.ApplyPinnedApplications(applications);
            }
        }
        catch (Exception exception)
        {
            if (RootFrame.Content is MainPage mainPage)
            {
                mainPage.SetPinnedApplicationsUnavailable(exception.Message);
            }
        }
    }

    private async Task<bool> OnPinnedApplicationMovedAsync(
        ShellCommand application,
        PinnedApplicationMoveDirection direction)
    {
        var applicationIds = PinnedApplicationIdList
            .Parse(_settings.PinnedApplicationIds);
        var reordered = PinnedApplicationOrder.Move(
            applicationIds,
            application.Id,
            direction);
        if (applicationIds.SequenceEqual(
                reordered,
                StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        var previousSettings = _settings;
        _settings = _settings with
        {
            PinnedApplicationIds =
                PinnedApplicationIdList.Serialize(reordered),
        };
        if (!PersistSettings())
        {
            _settings = previousSettings;
            throw new IOException(
                "The pinned application order could not be saved.");
        }

        _launcherWindow.SetPinnedApplicationIds(reordered);
        await RefreshPinnedApplicationsAsync().ConfigureAwait(true);
        return true;
    }

    private async Task RefreshAvailableApplicationsAsync()
    {
        try
        {
            _availableApplications = await _installedApplications
                .GetCommandsAsync(string.Empty, CancellationToken.None)
                .ConfigureAwait(true);
            foreach (var dockWindow in _dockWindows)
            {
                dockWindow.ApplyAvailableApplications(_availableApplications);
            }
        }
        catch (Exception exception)
        {
            if (RootFrame.Content is MainPage mainPage)
            {
                mainPage.SetPinnedApplicationsUnavailable(exception.Message);
            }
        }
    }

    private void OnLauncherShortcutChanged(LauncherShortcut shortcut)
    {
        if (_registeredShortcut == shortcut)
        {
            return;
        }

        if (!TryReplaceLauncherHotKey(shortcut, out var error))
        {
            if (RootFrame.Content is MainPage mainPage)
            {
                mainPage.SetShortcutUnavailable(shortcut, _registeredShortcut, error!);
            }

            return;
        }

        _settings = _settings with { LauncherShortcut = shortcut };
        var persisted = PersistSettings();
        if (RootFrame.Content is MainPage page)
        {
            page.SetShortcutApplied(shortcut, persisted);
        }
    }

    private void RegisterLauncherHotKey(LauncherShortcut shortcut)
    {
        if (!TryReplaceLauncherHotKey(shortcut, out var error) && RootFrame.Content is MainPage mainPage)
        {
            mainPage.SetShortcutUnavailable(shortcut, _registeredShortcut, error!);
        }
    }

    private bool TryReplaceLauncherHotKey(LauncherShortcut shortcut, out string? error)
    {
        var previousShortcut = _registeredShortcut;
        _launcherHotKey?.Dispose();
        _launcherHotKey = null;
        _registeredShortcut = null;

        try
        {
            _launcherHotKey = CreateLauncherHotKey(shortcut);
            _registeredShortcut = shortcut;
            error = null;
            return true;
        }
        catch (Exception exception)
        {
            error = exception.Message;
        }

        if (previousShortcut is not null)
        {
            try
            {
                _launcherHotKey = CreateLauncherHotKey(previousShortcut.Value);
                _registeredShortcut = previousShortcut;
            }
            catch (Exception restoreException)
            {
                error = $"{error} The previous shortcut could not be restored: {restoreException.Message}";
            }
        }

        return false;
    }

    private GlobalHotKey CreateLauncherHotKey(LauncherShortcut shortcut)
    {
        var modifiers = shortcut switch
        {
            LauncherShortcut.AltSpace => HotKeyModifiers.Alt,
            LauncherShortcut.ControlAltSpace => HotKeyModifiers.Control | HotKeyModifiers.Alt,
            LauncherShortcut.ControlShiftSpace => HotKeyModifiers.Control | HotKeyModifiers.Shift,
            _ => throw new ArgumentOutOfRangeException(nameof(shortcut), shortcut, null),
        };

        var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var hotKey = new GlobalHotKey(
            windowHandle,
            modifiers | HotKeyModifiers.NoRepeat,
            SpaceVirtualKey);
        hotKey.Pressed += OnLauncherRequested;
        return hotKey;
    }

    private bool PersistSettings()
    {
        try
        {
            _settingsStore.Save(_settings);
            return true;
        }
        catch (Exception exception)
        {
            if (RootFrame.Content is MainPage mainPage)
            {
                mainPage.SetSettingsSaveFailed(exception.Message);
            }

            return false;
        }
    }

    private async void OnGamingModeTimerTick(DispatcherQueueTimer sender, object args)
    {
        await RefreshGamingModeAsync().ConfigureAwait(true);
    }

    private async Task RefreshGamingModeAsync()
    {
        if (_refreshingGamingMode || !ShouldMonitorGames())
        {
            return;
        }

        _refreshingGamingMode = true;
        try
        {
            var startedAt = Stopwatch.GetTimestamp();
            using var currentProcess = Process.GetCurrentProcess();
            var processorTimeBefore = currentProcess.TotalProcessorTime;
            var configuredProcessNames = _gamingMode.ConfiguredProcessNames;
            var processes = await Task.Run(
                () => _processCatalog.CaptureByNames(configuredProcessNames)).ConfigureAwait(true);
            var scanDuration = Stopwatch.GetElapsedTime(startedAt);
            var processorTime = currentProcess.TotalProcessorTime - processorTimeBefore;
            _gamingDetectionPerformance.RecordSample(
                scanDuration,
                processorTime,
                GamingDetectionInterval,
                processes.Count);
            _gamingMode.Reconcile(processes);
        }
        catch (Exception exception)
        {
            if (RootFrame.Content is MainPage mainPage)
            {
                mainPage.SetGamingDetectionUnavailable(exception.Message);
            }
        }
        finally
        {
            _refreshingGamingMode = false;
        }
    }

    private void UpdateGamingModeMonitor()
    {
        if (!ShouldMonitorGames())
        {
            _gamingModeTimer.Stop();
            return;
        }

        _gamingModeTimer.Start();
        _ = RefreshGamingModeAsync();
    }

    private bool ShouldMonitorGames() =>
        _settings.AutomaticGamingModeEnabled &&
        GameDetector.ParseRules(_settings.GameProcessRules).Count > 0;

    private void OnLauncherRequested(object? sender, EventArgs e)
    {
        _ = _launcherWindow.ShowLauncherAsync();
    }

    private async void OnClosed(object sender, WindowEventArgs args)
    {
        _taskbarRefreshTimer.Stop();
        _ = SetDockWorkAreaReservation(false);
        _ = _taskbarReplacement.Disable();
        _displayChangeTimer.Stop();
        _clockRefreshTimer.Stop();
        _dockRefreshTimer.Stop();
        if (_displayChangeObserver is not null)
        {
            _displayChangeObserver.Changed -= OnDisplaysChanged;
            _displayChangeObserver.Dispose();
        }

        _gamingModeTimer.Stop();
        _launcherHotKey?.Dispose();
        _shellState.StateChanged -= OnShellStateChanged;
        _gamingMode.StatusChanged -= OnGamingSessionStatusChanged;
        if (_accessibility is not null)
        {
            _accessibility.Changed -= OnAccessibilityChanged;
            _accessibility.Dispose();
        }
        foreach (var dockWindow in _dockWindows)
        {
            dockWindow.PinChangedRequested -= OnPinnedApplicationChangedAsync;
            dockWindow.PinMoveRequested -= OnPinnedApplicationMovedAsync;
            dockWindow.Shutdown();
        }

        _launcherWindow.Shutdown();
        _launcherWindow.PinChangedRequested -= OnPinnedApplicationChangedAsync;
        await _pluginHost.DisposeAsync().ConfigureAwait(true);
        _taskbarReplacement.Dispose();
    }
}
