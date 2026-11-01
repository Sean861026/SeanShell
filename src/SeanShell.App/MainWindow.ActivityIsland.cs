using Microsoft.UI.Xaml;
using SeanShell.Core;

namespace SeanShell.App;

public sealed partial class MainWindow
{
    private ActivityIslandWindow? _activityIsland;
    private bool _activityIslandEnabled;

    private void OnActivityIslandToggleRequested(object? sender, EventArgs args)
    {
        _activityIslandEnabled = !_activityIslandEnabled;
        foreach (var dock in _dockWindows)
        {
            dock.SetActivityIslandEnabled(_activityIslandEnabled);
        }

        if (!_activityIslandEnabled)
        {
            _activityIsland?.Dismiss();
            return;
        }

        ShowActivityIsland("Activity Island enabled", "Preview for this session · tap to dismiss",
            sender is DockWindow source ? source.MonitorHandle : 0);
    }

    private void OnApplicationActivated(object? sender, DesktopWindowSnapshot window)
    {
        try
        {
            ShowActivityIsland(window.ProcessName, window.Title, _desktopWindows.CaptureForegroundMonitorHandle());
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            RetireActivityIsland(exception);
        }
    }

    private void ShowActivityIsland(string title, string detail, nint monitorHandle)
    {
        if (!_activityIslandEnabled || _isClosing || _exitRequested || _gamingMode.Current.IsGaming)
        {
            return;
        }

        try
        {
            var monitors = _displayMonitorService.Capture();
            var monitor = monitors.FirstOrDefault(candidate => candidate.Handle == monitorHandle)
                ?? monitors.FirstOrDefault(candidate => candidate.IsPrimary);
            if (monitor is null || !ActivityIslandPolicy.CanShow(
                _activityIslandEnabled,
                _gamingMode.Current.IsGaming,
                _desktopWindows.CaptureTopApplicationFullscreenMonitorHandles().Contains(monitor.Handle),
                _isClosing || _exitRequested))
            {
                _activityIsland?.Dismiss();
                return;
            }

            if (_activityIsland is { IsClosed: true })
            {
                _activityIsland = null;
            }
            _activityIsland ??= new ActivityIslandWindow();
            _activityIsland.SetReducedEffects(_systemAccessibility.ReducedEffects);
            _activityIsland.ShowActivity(title, detail, monitor, _systemAccessibility.TextScaleFactor);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Optional presentation must never turn a successful app switch into a shell failure.
            RetireActivityIsland(exception);
        }
    }

    private void ReconcileActivityIsland()
    {
        if (_activityIsland is not { IsVisible: true } island)
        {
            return;
        }

        try
        {
            if (!ActivityIslandPolicy.CanShow(_activityIslandEnabled, _gamingMode.Current.IsGaming,
                _desktopWindows.CaptureTopApplicationFullscreenMonitorHandles().Contains(island.MonitorHandle),
                _isClosing || _exitRequested))
            {
                island.Dismiss();
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            RetireActivityIsland(exception);
        }
    }

    private void RetireActivityIsland(Exception exception)
    {
        // Detach first: failed cleanup must never leave a broken presentation
        // available to later activation, accessibility, or shutdown callbacks.
        var failedIsland = _activityIsland;
        _activityIsland = null;
        _ = ActivityIslandCleanup.TryRun(() =>
            ((App)Application.Current).Diagnostics.TryWrite(DiagnosticEventKind.DockActionFailed, exception));
        _ = ActivityIslandCleanup.TryRun(() => failedIsland?.Shutdown(), cleanupFailure =>
            ((App)Application.Current).Diagnostics.TryWrite(DiagnosticEventKind.DockActionFailed, cleanupFailure));
    }
}
