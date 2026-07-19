using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SeanShell.Core;
using SeanShell.Windows;

namespace SeanShell.App;

public sealed partial class MainPage : Page
{
    private readonly ShellStateStore _shellState;
    private readonly DesktopWindowService _desktopWindows;
    private readonly SystemMetricsProvider _systemMetrics;
    private readonly int _displayCount;
    private readonly DispatcherQueueTimer _refreshTimer;
    private bool _applyingSettings;
    private bool _refreshing;

    public MainPage()
    {
        InitializeComponent();

        var app = (App)Application.Current;
        _shellState = app.ShellState;
        _desktopWindows = app.DesktopWindows;
        _systemMetrics = app.SystemMetrics;
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

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _shellState.StateChanged -= OnShellStateChanged;
        _shellState.StateChanged += OnShellStateChanged;
        ApplyShellState(_shellState.Current);
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
    }

    private void OnOpenLauncherClicked(object sender, RoutedEventArgs e)
    {
        LauncherRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnGamingModeToggled(object sender, RoutedEventArgs e)
    {
        _shellState.SetMode(GamingModeToggle.IsOn ? ShellMode.Gaming : ShellMode.Normal);
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

    private void ApplyShellState(ShellState state)
    {
        var gaming = state.Mode == ShellMode.Gaming;
        GamingModeToggle.IsOn = gaming;
        ModeText.Text = gaming ? "Gaming" : "Normal";
        ProviderStatus.Text = gaming ? "Providers paused" : "Providers active";
        UpdateDockStatus(gaming);
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
