using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SeanShell.Core;
using SeanShell.Gaming;
using SeanShell.Plugins;
using SeanShell.Windows;

namespace SeanShell.App;

public sealed partial class MainPage : Page
{
    private readonly ShellStateStore _shellState;
    private readonly DesktopWindowService _desktopWindows;
    private readonly SystemMetricsProvider _systemMetrics;
    private readonly int _displayCount;
    private readonly GamingModeManager _gamingMode;
    private readonly PluginHost _pluginHost;
    private readonly HashSet<string> _pendingPluginIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherQueueTimer _refreshTimer;
    private bool _applyingSettings;
    private bool _applyingPluginDiagnostics;
    private bool _refreshing;

    public MainPage()
    {
        InitializeComponent();

        var app = (App)Application.Current;
        _shellState = app.ShellState;
        _desktopWindows = app.DesktopWindows;
        _systemMetrics = app.SystemMetrics;
        _gamingMode = app.GamingMode;
        _pluginHost = app.PluginHost;
        _displayCount = app.Displays.Capture().Count;

        _refreshTimer = DispatcherQueue.CreateTimer();
        _refreshTimer.Interval = TimeSpan.FromSeconds(2);
        _refreshTimer.Tick += OnRefreshTimerTick;
        ApplySettings(app.SettingsLoad.Settings);
        if (app.SettingsLoad.Warning is not null)
        {
            SetSettingsStatus(
                app.SettingsLoad.WasRecovered ? "Settings recovered" : "Safe settings active",
                app.SettingsLoad.Warning,
                InfoBarSeverity.Warning);
        }
    }

    public event EventHandler? LauncherRequested;

    public event Action<bool>? DockAutoHideChanged;

    public event Action<LauncherShortcut>? LauncherShortcutChanged;

    public event Action<bool>? AutomaticGamingModeChanged;

    public event Action<string>? GameProcessRulesSaved;

    public event Action<bool>? ManualGamingModeChanged;

    public event Action<string, bool>? PluginEnabledChanged;

    public void SetShortcutApplied(LauncherShortcut shortcut, bool persisted = true)
    {
        SelectShortcut(shortcut);
        ShortcutStatus.Text = $"Keyboard shortcut: {shortcut.GetDisplayName()}";
        SetSettingsStatus(
            persisted ? "Shortcut updated" : "Shortcut active for this session",
            persisted
                ? $"{shortcut.GetDisplayName()} now opens the Launcher."
                : $"{shortcut.GetDisplayName()} works now, but the settings file could not be updated.",
            persisted ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
    }

    public void SetShortcutUnavailable(LauncherShortcut requested, LauncherShortcut? restored, string reason)
    {
        if (restored is not null)
        {
            SelectShortcut(restored.Value);
            ShortcutStatus.Text = $"Keyboard shortcut: {restored.Value.GetDisplayName()}";
        }
        else
        {
            _applyingSettings = true;
            LauncherShortcutComboBox.SelectedItem = null;
            _applyingSettings = false;
            ShortcutStatus.Text = "No keyboard shortcut is active. Use the Open Launcher button.";
        }

        SetSettingsStatus(
            "Shortcut unavailable",
            restored is null
                ? $"Windows could not register {requested.GetDisplayName()}. Use Open Launcher or choose another shortcut. {reason}"
                : $"Windows could not register {requested.GetDisplayName()}. {restored.Value.GetDisplayName()} remains active. {reason}",
            InfoBarSeverity.Warning);
    }

    public void SetSettingsSaveFailed(string message)
    {
        SetSettingsStatus("Settings not saved", message, InfoBarSeverity.Warning);
    }

    public void SetGamingSettingsApplied(string title, string message)
    {
        SetSettingsStatus(title, message, InfoBarSeverity.Success);
    }

    public void SetGameProcessRulesApplied(string rules, int count, bool persisted)
    {
        _applyingSettings = true;
        GameProcessRulesTextBox.Text = rules;
        _applyingSettings = false;
        if (persisted)
        {
            SetSettingsStatus(
                "Game rules updated",
                count == 0 ? "No automatic game rules are configured." : $"Saved {count} game process rule(s).",
                InfoBarSeverity.Success);
        }
    }

    public void SetGamingDetectionUnavailable(string message)
    {
        SetSettingsStatus(
            "Game detection paused",
            $"SeanShell could not read the current process snapshot and will retry automatically. {message}",
            InfoBarSeverity.Warning);
    }

    public void SetPluginEnabledApplied(string pluginId, string pluginName, bool enabled)
    {
        _pendingPluginIds.Remove(pluginId);
        ApplyPluginDiagnostics();
        SetSettingsStatus(
            enabled ? "Plugin enabled" : "Plugin disabled",
            enabled
                ? $"{pluginName} is available to Launcher and normal-mode providers."
                : $"{pluginName} is disabled and will remain off after restart.",
            InfoBarSeverity.Success);
    }

    public void SetPluginEnabledFailed(string pluginId, string message)
    {
        _pendingPluginIds.Remove(pluginId);
        ApplyPluginDiagnostics();
        SetSettingsStatus("Plugin setting not changed", message, InfoBarSeverity.Warning);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _shellState.StateChanged -= OnShellStateChanged;
        _shellState.StateChanged += OnShellStateChanged;
        _gamingMode.StatusChanged -= OnGamingModeStatusChanged;
        _gamingMode.StatusChanged += OnGamingModeStatusChanged;
        _pluginHost.DiagnosticsChanged -= OnPluginDiagnosticsChanged;
        _pluginHost.DiagnosticsChanged += OnPluginDiagnosticsChanged;
        ApplyShellState(_shellState.Current);
        ApplyGamingModeStatus(_gamingMode.Current);
        ApplyPluginDiagnostics();
        if (_shellState.Current.Mode == ShellMode.Normal)
        {
            _refreshTimer.Start();
            _ = RefreshDashboardAsync();
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _refreshTimer.Stop();
        _shellState.StateChanged -= OnShellStateChanged;
        _gamingMode.StatusChanged -= OnGamingModeStatusChanged;
        _pluginHost.DiagnosticsChanged -= OnPluginDiagnosticsChanged;
    }

    private void OnOpenLauncherClicked(object sender, RoutedEventArgs e)
    {
        LauncherRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnGamingModeToggled(object sender, RoutedEventArgs e)
    {
        if (!_applyingSettings)
        {
            ManualGamingModeChanged?.Invoke(GamingModeToggle.IsOn);
        }
    }

    private void OnAutomaticGamingModeToggled(object sender, RoutedEventArgs e)
    {
        if (!_applyingSettings)
        {
            AutomaticGamingModeChanged?.Invoke(AutomaticGamingModeToggle.IsOn);
        }
    }

    private void OnSaveGameRulesClicked(object sender, RoutedEventArgs e)
    {
        GameProcessRulesSaved?.Invoke(GameProcessRulesTextBox.Text);
    }

    private void OnPluginEnabledToggled(object sender, RoutedEventArgs e)
    {
        if (_applyingPluginDiagnostics ||
            sender is not ToggleSwitch toggle ||
            toggle.Tag is not string pluginId ||
            _pendingPluginIds.Contains(pluginId))
        {
            return;
        }

        _pendingPluginIds.Add(pluginId);
        ApplyPluginDiagnostics();
        if (PluginEnabledChanged is null)
        {
            _pendingPluginIds.Remove(pluginId);
            ApplyPluginDiagnostics();
            return;
        }

        PluginEnabledChanged.Invoke(pluginId, toggle.IsOn);
    }

    private void OnDockAutoHideToggled(object sender, RoutedEventArgs e)
    {
        if (_applyingSettings)
        {
            return;
        }

        DockAutoHideChanged?.Invoke(DockAutoHideToggle.IsOn);
        UpdateDockStatus(_shellState.Current.Mode == ShellMode.Gaming);
    }

    private void OnLauncherShortcutSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_applyingSettings || LauncherShortcutComboBox.SelectedItem is not ComboBoxItem item)
        {
            return;
        }

        if (Enum.TryParse<LauncherShortcut>(item.Tag?.ToString(), out var shortcut))
        {
            LauncherShortcutChanged?.Invoke(shortcut);
        }
    }

    private void OnShellStateChanged(object? sender, ShellState state)
    {
        ApplyShellState(state);
        if (state.Mode == ShellMode.Gaming)
        {
            _refreshTimer.Stop();
            return;
        }

        _refreshTimer.Start();
        _ = RefreshDashboardAsync();
    }

    private void OnGamingModeStatusChanged(object? sender, GamingModeStatus status)
    {
        ApplyGamingModeStatus(status);
    }

    private void OnPluginDiagnosticsChanged(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(ApplyPluginDiagnostics);
    }

    private void ApplyShellState(ShellState state)
    {
        var gaming = state.Mode == ShellMode.Gaming;
        ModeText.Text = gaming ? "Gaming" : "Normal";
        ProviderStatus.Text = gaming ? "Providers paused" : "Providers active";
        UpdateDockStatus(gaming);
    }

    private void ApplyGamingModeStatus(GamingModeStatus status)
    {
        _applyingSettings = true;
        GamingModeToggle.IsOn = status.ManualModeEnabled;
        AutomaticGamingModeToggle.IsOn = status.AutomaticDetectionEnabled;
        _applyingSettings = false;

        GamingDetectionStatus.Text = status.ManualModeEnabled
            ? status.ActiveGameNames.Count > 0
                ? $"Manual mode active; also detected: {string.Join(", ", status.ActiveGameNames)}"
                : "Manual gaming mode is active"
            : status.ActiveGameNames.Count > 0
                ? $"Automatically detected: {string.Join(", ", status.ActiveGameNames)}"
                : status.AutomaticDetectionEnabled
                    ? status.ConfiguredRuleCount > 0
                        ? "Watching configured game processes"
                        : "Automatic detection is on; add at least one process name"
                    : "Automatic detection is off";
    }

    private void ApplyPluginDiagnostics()
    {
        var diagnostics = _pluginHost.Diagnostics;
        _applyingPluginDiagnostics = true;
        try
        {
            PluginDiagnosticsList.ItemsSource = diagnostics
                .Select(diagnostic => new PluginDiagnosticViewModel(
                    diagnostic,
                    !_pendingPluginIds.Contains(diagnostic.Id)))
                .ToArray();
        }
        finally
        {
            _applyingPluginDiagnostics = false;
        }

        var active = diagnostics.Count(static diagnostic => diagnostic.State == PluginRuntimeState.Active);
        var suspended = diagnostics.Count(static diagnostic => diagnostic.State == PluginRuntimeState.Suspended);
        var faulted = diagnostics.Count(static diagnostic => diagnostic.State == PluginRuntimeState.Faulted);
        var disabled = diagnostics.Count(static diagnostic => !diagnostic.IsEnabled);
        PluginStatusSummary.Text = diagnostics.Count == 0
            ? "No built-in plugins are registered"
            : $"{diagnostics.Count} registered · {active} active · {suspended} suspended · {disabled} disabled · {faulted} faulted";
        PluginEmptyState.Visibility = diagnostics.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        PluginDiagnosticsList.Visibility = diagnostics.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void UpdateDockStatus(bool gaming)
    {
        DockStatus.Text = gaming
            ? $"Hidden during gaming mode on {_displayCount} display(s)"
            : DockAutoHideToggle.IsOn
                ? $"Auto-hide active on {_displayCount} display(s)"
                : $"Expanded on {_displayCount} display(s)";
    }

    private async void OnRefreshTimerTick(DispatcherQueueTimer sender, object args)
    {
        await RefreshDashboardAsync().ConfigureAwait(true);
    }

    private async Task RefreshDashboardAsync()
    {
        if (_refreshing || _shellState.Current.Mode == ShellMode.Gaming)
        {
            return;
        }

        _refreshing = true;
        try
        {
            var snapshot = await Task.Run(() =>
            {
                var metrics = _systemMetrics.Capture();
                var windowCount = _desktopWindows.Capture().Count;
                return (metrics, windowCount);
            }).ConfigureAwait(true);

            CpuValue.Text = $"{snapshot.metrics.CpuUsagePercent:F0}%";
            CpuProgress.Value = snapshot.metrics.CpuUsagePercent;
            MemoryValue.Text = $"{FormatGiB(snapshot.metrics.UsedPhysicalMemoryBytes)} / {FormatGiB(snapshot.metrics.TotalPhysicalMemoryBytes)}";
            MemoryProgress.Value = snapshot.metrics.MemoryUsagePercent;
            WindowCountValue.Text = snapshot.windowCount.ToString();
            DashboardStatus.Severity = InfoBarSeverity.Informational;
            DashboardStatus.Title = "Compatibility-first";
            DashboardStatus.Message = "Explorer remains active; the dock only enumerates supported top-level windows.";
        }
        catch (Exception exception)
        {
            DashboardStatus.Severity = InfoBarSeverity.Warning;
            DashboardStatus.Title = "Dashboard update paused";
            DashboardStatus.Message = exception.Message;
        }
        finally
        {
            _refreshing = false;
        }
    }

    private static string FormatGiB(ulong bytes) => $"{bytes / 1_073_741_824d:F1} GB";

    private void ApplySettings(ShellSettings settings)
    {
        _applyingSettings = true;
        DockAutoHideToggle.IsOn = settings.DockAutoHide;
        AutomaticGamingModeToggle.IsOn = settings.AutomaticGamingModeEnabled;
        GameProcessRulesTextBox.Text = settings.GameProcessRules;
        SelectShortcut(settings.LauncherShortcut);
        ShortcutStatus.Text = $"Keyboard shortcut: {settings.LauncherShortcut.GetDisplayName()}";
        _applyingSettings = false;
    }

    private void SelectShortcut(LauncherShortcut shortcut)
    {
        var wasApplyingSettings = _applyingSettings;
        _applyingSettings = true;
        LauncherShortcutComboBox.SelectedItem = LauncherShortcutComboBox.Items
            .OfType<ComboBoxItem>()
            .First(item => string.Equals(item.Tag?.ToString(), shortcut.ToString(), StringComparison.Ordinal));
        _applyingSettings = wasApplyingSettings;
    }

    private void SetSettingsStatus(string title, string message, InfoBarSeverity severity)
    {
        SettingsStatus.Title = title;
        SettingsStatus.Message = message;
        SettingsStatus.Severity = severity;
        SettingsStatus.IsOpen = true;
    }
}
