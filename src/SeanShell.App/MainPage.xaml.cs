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
    private readonly DispatcherQueueTimer _refreshTimer;
    private bool _refreshing;

    public MainPage()
    {
        InitializeComponent();

        var app = (App)Application.Current;
        _shellState = app.ShellState;
        _desktopWindows = app.DesktopWindows;
        _systemMetrics = app.SystemMetrics;

        _refreshTimer = DispatcherQueue.CreateTimer();
        _refreshTimer.Interval = TimeSpan.FromSeconds(2);
        _refreshTimer.Tick += OnRefreshTimerTick;
    }

    public event EventHandler? LauncherRequested;

    public void SetShortcutUnavailable(string reason)
    {
        ShortcutStatus.Text = $"Alt + Space is unavailable. Use the button instead. {reason}";
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
        DockStatus.Text = gaming ? "Hidden during gaming mode" : "Visible above the primary taskbar";
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
}
