using System.Collections.ObjectModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SeanShell.Core;
using SeanShell.Windows;
using Windows.Graphics;

namespace SeanShell.App;

public sealed partial class DockWindow : Window
{
    private const int DockWidth = 760;
    private const int DockHeight = 80;
    private const int PeekWidth = 160;
    private const int PeekHeight = 10;
    private readonly DesktopWindowService _windowService;
    private readonly ShellStateStore _shellState;
    private readonly DisplayMonitorSnapshot _monitor;
    private readonly DispatcherQueueTimer _refreshTimer;
    private readonly DispatcherQueueTimer _autoHideTimer;
    private bool _allowClose;
    private bool _autoHide = true;
    private bool _collapsed;
    private bool _hasKeyboardFocus;
    private bool _pointerInside;
    private bool _refreshing;

    public DockWindow(
        DesktopWindowService windowService,
        ShellStateStore shellState,
        DisplayMonitorSnapshot monitor)
    {
        _windowService = windowService;
        _shellState = shellState;
        _monitor = monitor;
        InitializeComponent();

        WindowList.ItemsSource = Items;
        AppWindow.SetIcon("Assets/AppIcon.ico");
        ConfigurePresenter();

        _refreshTimer = DispatcherQueue.CreateTimer();
        _refreshTimer.Interval = TimeSpan.FromSeconds(2);
        _refreshTimer.Tick += OnRefreshTimerTick;

        _autoHideTimer = DispatcherQueue.CreateTimer();
        _autoHideTimer.Interval = TimeSpan.FromMilliseconds(900);
        _autoHideTimer.IsRepeating = false;
        _autoHideTimer.Tick += OnAutoHideTimerTick;

        _shellState.StateChanged += OnShellStateChanged;
        AppWindow.Closing += OnWindowClosing;
    }

    public ObservableCollection<DockItemViewModel> Items { get; } = [];

    public void ShowDock()
    {
        SetCollapsed(false);
        EmptyState.Visibility = Visibility.Visible;
        AppWindow.Show();
        _refreshTimer.Start();
        _ = RefreshWindowsAsync();
        ScheduleAutoHide();
    }

    public void SetAutoHide(bool enabled)
    {
        _autoHide = enabled;
        if (!enabled)
        {
            _autoHideTimer.Stop();
            SetCollapsed(false);
            return;
        }

        ScheduleAutoHide();
    }

    public void Shutdown()
    {
        _refreshTimer.Stop();
        _autoHideTimer.Stop();
        _shellState.StateChanged -= OnShellStateChanged;
        _allowClose = true;
        Close();
    }

    private void ConfigurePresenter()
    {
        var presenter = OverlappedPresenter.Create();
        presenter.IsAlwaysOnTop = true;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsResizable = false;
        presenter.SetBorderAndTitleBar(false, false);
        AppWindow.SetPresenter(presenter);
    }

    private void SetCollapsed(bool collapsed)
    {
        _collapsed = collapsed;
        ExpandedDock.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        PeekIndicator.Visibility = collapsed ? Visibility.Visible : Visibility.Collapsed;

        var bounds = DockPlacement.Calculate(
            _monitor,
            DockWidth,
            DockHeight,
            collapsed,
            PeekWidth,
            PeekHeight);
        AppWindow.Resize(new SizeInt32(bounds.Width, bounds.Height));
        AppWindow.Move(new PointInt32(bounds.X, bounds.Y));
    }

    private void ScheduleAutoHide()
    {
        if (!_autoHide || _shellState.Current.Mode == ShellMode.Gaming)
        {
            return;
        }

        _autoHideTimer.Stop();
        _autoHideTimer.Start();
    }

    private async Task RefreshWindowsAsync()
    {
        if (_refreshing)
        {
            return;
        }

        _refreshing = true;
        try
        {
            var windows = await Task.Run(() => _windowService.Capture()
                .Where(window => window.MonitorHandle == _monitor.Handle)
                .Take(12)
                .ToArray()).ConfigureAwait(true);
            if (_allowClose)
            {
                return;
            }

            if (windows.Length == Items.Count && windows
                .Select(static window => (window.Handle, window.Title, window.ProcessName))
                .SequenceEqual(Items.Select(static item => (item.Handle, item.Title, item.ProcessName))))
            {
                return;
            }

            Items.Clear();
            foreach (var window in windows)
            {
                Items.Add(new DockItemViewModel(window));
            }

            EmptyStateText.Text = $"No open application windows on {_monitor.DeviceName}";
            EmptyState.Visibility = Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception exception)
        {
            Items.Clear();
            EmptyStateText.Text = $"Dock unavailable: {exception.Message}";
            EmptyState.Visibility = Visibility.Visible;
        }
        finally
        {
            _refreshing = false;
        }
    }

    private async void OnRefreshTimerTick(DispatcherQueueTimer sender, object args)
    {
        await RefreshWindowsAsync().ConfigureAwait(true);
    }

    private void OnAutoHideTimerTick(DispatcherQueueTimer sender, object args)
    {
        if (!_pointerInside && !_hasKeyboardFocus && _autoHide)
        {
            SetCollapsed(true);
        }
    }

    private void OnDockPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _pointerInside = true;
        _autoHideTimer.Stop();
        if (_collapsed)
        {
            SetCollapsed(false);
        }
    }

    private void OnDockPointerExited(object sender, PointerRoutedEventArgs e)
    {
        _pointerInside = false;
        ScheduleAutoHide();
    }

    private void OnDockGotFocus(object sender, RoutedEventArgs e)
    {
        _hasKeyboardFocus = true;
        _autoHideTimer.Stop();
        if (_collapsed)
        {
            SetCollapsed(false);
        }
    }

    private void OnDockLostFocus(object sender, RoutedEventArgs e)
    {
        _hasKeyboardFocus = false;
        ScheduleAutoHide();
    }

    private void OnWindowClicked(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is DockItemViewModel item)
        {
            _ = _windowService.Activate(item.Handle);
            ScheduleAutoHide();
        }
    }

    private void OnShellStateChanged(object? sender, ShellState state)
    {
        if (state.Mode == ShellMode.Gaming)
        {
            _refreshTimer.Stop();
            _autoHideTimer.Stop();
            AppWindow.Hide();
            return;
        }

        SetCollapsed(false);
        AppWindow.Show();
        _refreshTimer.Start();
        _ = RefreshWindowsAsync();
        ScheduleAutoHide();
    }

    private void OnWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_allowClose)
        {
            return;
        }

        args.Cancel = true;
        AppWindow.Hide();
    }
}
