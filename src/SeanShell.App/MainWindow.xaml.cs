using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
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
    private readonly DesktopWindowService _desktopWindows;
    private readonly DisplayMonitorService _displayMonitorService;
    private readonly DispatcherQueueTimer _displayChangeTimer;
    private readonly DispatcherQueueTimer _dockRefreshTimer;
    private readonly LauncherWindow _launcherWindow;
    private readonly GamingModeManager _gamingMode;
    private readonly DispatcherQueueTimer _gamingModeTimer;
    private readonly ProcessCatalog _processCatalog;
    private readonly PluginHost _pluginHost;
    private readonly ShellStateStore _shellState;
    private readonly ShellSettingsStore _settingsStore;
    private DisplayChangeObserver? _displayChangeObserver;
    private IReadOnlyList<DisplayMonitorSnapshot> _monitors;
    private IReadOnlyList<DockWindow> _dockWindows;
    private bool _refreshingDockWindows;
    private bool _refreshingGamingMode;
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
        _pluginHost = app.PluginHost;
        _processCatalog = app.Processes;
        _desktopWindows = app.DesktopWindows;
        _displayMonitorService = app.Displays;
        _shellState = app.ShellState;
        _launcherWindow = new LauncherWindow(app.LauncherSearch, app.LauncherPerformance);
        _monitors = _displayMonitorService.Capture();
        _dockWindows = CreateDockWindows(_monitors);

        if (RootFrame.Content is MainPage mainPage)
        {
            mainPage.LauncherRequested += OnLauncherRequested;
            mainPage.DockAutoHideChanged += OnDockAutoHideChanged;
            mainPage.LauncherShortcutChanged += OnLauncherShortcutChanged;
            mainPage.AutomaticGamingModeChanged += OnAutomaticGamingModeChanged;
            mainPage.GameProcessRulesSaved += OnGameProcessRulesSaved;
            mainPage.ManualGamingModeChanged += OnManualGamingModeChanged;
            mainPage.PluginEnabledChanged += OnPluginEnabledChanged;
        }

        _gamingModeTimer = DispatcherQueue.CreateTimer();
        _gamingModeTimer.Interval = TimeSpan.FromSeconds(2);
        _gamingModeTimer.Tick += OnGamingModeTimerTick;

        _dockRefreshTimer = DispatcherQueue.CreateTimer();
        _dockRefreshTimer.Interval = TimeSpan.FromSeconds(2);
        _dockRefreshTimer.Tick += OnDockRefreshTimerTick;

        _displayChangeTimer = DispatcherQueue.CreateTimer();
        _displayChangeTimer.Interval = TimeSpan.FromMilliseconds(500);
        _displayChangeTimer.IsRepeating = false;
        _displayChangeTimer.Tick += OnDisplayChangeTimerTick;
        TryObserveDisplayChanges();

        RegisterLauncherHotKey(_settings.LauncherShortcut);
        _shellState.StateChanged += OnShellStateChanged;
        Activated += OnActivated;
        Closed += OnClosed;
    }

    private async void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        Activated -= OnActivated;
        foreach (var dockWindow in _dockWindows)
        {
            dockWindow.ShowDock();
            dockWindow.SetAutoHide(_settings.DockAutoHide);
        }

        _dockRefreshTimer.Start();
        _ = RefreshDockWindowsAsync();
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
                _dockRefreshTimer.Stop();
                await _pluginHost.SuspendAsync().ConfigureAwait(true);
            }
            else
            {
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

    private void OnAutomaticGamingModeChanged(bool enabled)
    {
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
                dockWindow.Shutdown();
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
                windows.Add(new DockWindow(_desktopWindows, _shellState, monitor));
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
            var processes = await Task.Run(_processCatalog.Capture).ConfigureAwait(true);
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
        _displayChangeTimer.Stop();
        _dockRefreshTimer.Stop();
        if (_displayChangeObserver is not null)
        {
            _displayChangeObserver.Changed -= OnDisplaysChanged;
            _displayChangeObserver.Dispose();
        }

        _gamingModeTimer.Stop();
        _launcherHotKey?.Dispose();
        _shellState.StateChanged -= OnShellStateChanged;
        foreach (var dockWindow in _dockWindows)
        {
            dockWindow.Shutdown();
        }

        _launcherWindow.Shutdown();
        await _pluginHost.DisposeAsync().ConfigureAwait(true);
    }
}
