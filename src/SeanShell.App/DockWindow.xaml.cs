using System.Collections.ObjectModel;
using System.Globalization;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SeanShell.Core;
using SeanShell.Windows;
using Windows.Graphics;

namespace SeanShell.App;

public sealed partial class DockWindow : Window
{
    private const int DockHeight = 76;
    private const int PeekWidth = 180;
    private const int PeekHeight = 12;
    private const int CompactDockHeight = 72;
    private const int WorkAreaVerticalMargin = 16;
    private readonly DesktopWindowService _windowService;
    private readonly ShellStateStore _shellState;
    private readonly DisplayMonitorSnapshot _monitor;
    private readonly AppBarWorkAreaReservation _workAreaReservation = new();
    private readonly DispatcherQueueTimer _autoHideTimer;
    private readonly bool _compactDensity;
    private double _textScaleFactor = 1;
    private bool _allowClose;
    private bool _autoHide = true;
    private bool _collapsed;
    private bool _contextMenuOpen;
    private bool _hasKeyboardFocus;
    private bool _pointerInside;
    private int _expandedDockWidth;
    private IReadOnlyList<ShellCommand> _pinnedApplications = [];
    private IReadOnlyList<DesktopWindowSnapshot> _monitorWindows = [];
    private DockBounds? _reservedArea;

    public DockWindow(
        DesktopWindowService windowService,
        ShellStateStore shellState,
        DisplayMonitorSnapshot monitor,
        double textScaleFactor)
    {
        _windowService = windowService;
        _shellState = shellState;
        _monitor = monitor;
        InitializeComponent();

        var density = ((App)Application.Current).SettingsLoad.Settings.DisplayDensity;
        _compactDensity = density == ShellDisplayDensity.Compact;
        _textScaleFactor = textScaleFactor;
        _expandedDockWidth = TaskbarDockLayout.CalculateExpandedWidth(
            0,
            0,
            _monitor.WorkAreaWidth);
        ApplyDisplayDensity(density);
        PinnedList.ItemsSource = PinnedItems;
        WindowList.ItemsSource = Items;
        AppWindow.SetIcon("Assets/AppIcon.ico");
        ConfigurePresenter();

        _autoHideTimer = DispatcherQueue.CreateTimer();
        _autoHideTimer.Interval = TimeSpan.FromMilliseconds(900);
        _autoHideTimer.IsRepeating = false;
        _autoHideTimer.Tick += OnAutoHideTimerTick;

        _shellState.StateChanged += OnShellStateChanged;
        AppWindow.Closing += OnWindowClosing;
    }

    public ObservableCollection<DockItemViewModel> Items { get; } = [];

    public ObservableCollection<PinnedDockItemViewModel> PinnedItems { get; } = [];

    public event EventHandler? LauncherRequested;

    public event EventHandler? SystemAreaRequested;

    private void ApplyDisplayDensity(ShellDisplayDensity density)
    {
        if (density != ShellDisplayDensity.Compact)
        {
            return;
        }

        ExpandedDock.Padding = new Thickness(4);
        PinnedList.ItemContainerStyle =
            (Style)Application.Current.Resources["SeanCompactDockItemStyle"];
        WindowList.ItemContainerStyle =
            (Style)Application.Current.Resources["SeanCompactDockItemStyle"];
    }

    public void ShowDock()
    {
        SetCollapsed(false);
        EmptyState.Visibility = Visibility.Visible;
        AppWindow.Show();
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

    public void ApplyTextScaleFactor(double textScaleFactor)
    {
        _textScaleFactor = textScaleFactor;
        SetCollapsed(_collapsed);
    }

    public WorkAreaReservationResult SetWorkAreaReservation(bool enabled)
    {
        if (!enabled)
        {
            var released = _workAreaReservation.Release();
            if (released.Success)
            {
                _reservedArea = null;
                SetCollapsed(_collapsed);
            }

            return released;
        }

        var dockHeight = AccessibilityLayout.ScaleDockHeight(
            _compactDensity ? CompactDockHeight : DockHeight,
            _textScaleFactor);
        var result = _workAreaReservation.Reserve(
            WinRT.Interop.WindowNative.GetWindowHandle(this),
            _monitor.Handle,
            dockHeight + WorkAreaVerticalMargin);
        _reservedArea = result.Success ? result.ReservedArea : null;
        SetCollapsed(_collapsed);
        return result;
    }

    public void ApplyClock(DateTimeOffset timestamp)
    {
        var local = timestamp.LocalDateTime;
        var culture = CultureInfo.CurrentCulture;
        ClockTimeText.Text = local.ToString("t", culture);
        ClockDateText.Text = local.ToString("d", culture);
        AutomationProperties.SetName(
            ClockTimeText,
            $"Current date and time: {local.ToString("F", culture)}");
    }

    public void SetSystemAreaAccessState(bool available, bool revealed)
    {
        SystemAreaButton.Visibility =
            available ? Visibility.Visible : Visibility.Collapsed;
        var label = revealed
            ? "Resume SeanShell taskbar replacement"
            : "Show Windows system area";
        AutomationProperties.SetName(SystemAreaButton, label);
        ToolTipService.SetToolTip(
            SystemAreaButton,
            revealed
                ? "Hide the Windows taskbar and resume SeanShell replacement"
                : "Show the Windows taskbar for notification and system tray access");
        SystemAreaGlyph.Glyph = "\uE7F4";
    }

    public void Shutdown()
    {
        _autoHideTimer.Stop();
        _shellState.StateChanged -= OnShellStateChanged;
        _ = _workAreaReservation.Release();
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

        var placementMonitor = _reservedArea is null
            ? _monitor
            : _monitor with
            {
                WorkAreaX = _reservedArea.X,
                WorkAreaY = _reservedArea.Y,
                WorkAreaWidth = _reservedArea.Width,
                WorkAreaHeight = _reservedArea.Height,
            };
        var bounds = DockPlacement.Calculate(
            placementMonitor,
            _expandedDockWidth,
            AccessibilityLayout.ScaleDockHeight(
                _compactDensity ? CompactDockHeight : DockHeight,
                _textScaleFactor),
            collapsed,
            PeekWidth,
            PeekHeight);
        AppWindow.Resize(new SizeInt32(bounds.Width, bounds.Height));
        AppWindow.Move(new PointInt32(bounds.X, bounds.Y));
    }

    private void ScheduleAutoHide()
    {
        if (!_autoHide ||
            _contextMenuOpen ||
            _shellState.Current.Mode == ShellMode.Gaming)
        {
            return;
        }

        _autoHideTimer.Stop();
        _autoHideTimer.Start();
    }

    public void ApplyWindowSnapshot(IReadOnlyList<DesktopWindowSnapshot> snapshot)
    {
        if (_allowClose)
        {
            return;
        }

        var windows = DesktopWindowFilter.ForMonitor(snapshot, _monitor.Handle);
        if (windows.Count == Items.Count && windows
            .Select(static window => (
                window.Handle,
                window.Title,
                window.ProcessName,
                window.IsMinimized,
                window.IsForeground))
            .SequenceEqual(Items.Select(static item => (
                item.Handle,
                item.Title,
                item.ProcessName,
                item.IsMinimized,
                item.IsForeground))))
        {
            return;
        }

        _monitorWindows = windows;
        Items.Clear();
        foreach (var window in windows)
        {
            Items.Add(new DockItemViewModel(window));
        }

        WindowList.SelectedItem = Items.FirstOrDefault(static item => item.IsForeground);
        DockCountText.Text = Items.Count == 1 ? "1 window" : $"{Items.Count} windows";
        EmptyStateText.Text = $"No open application windows on {_monitor.DeviceName}";
        EmptyState.Visibility = Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RefreshPinnedItems();
    }

    public void ApplyPinnedApplications(
        IReadOnlyList<ShellCommand> applications)
    {
        if (_allowClose ||
            applications.Select(static command => command.Id).SequenceEqual(
                _pinnedApplications.Select(static command => command.Id),
                StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        _pinnedApplications = applications.ToArray();
        RefreshPinnedItems();
    }

    public void SetWindowSnapshotUnavailable(string message)
    {
        if (_allowClose)
        {
            return;
        }

        _monitorWindows = [];
        Items.Clear();
        WindowList.SelectedItem = null;
        DockCountText.Text = "Unavailable";
        EmptyStateText.Text = $"Dock unavailable: {message}";
        EmptyState.Visibility = Visibility.Visible;
        RefreshPinnedItems();
    }

    private void OnAutoHideTimerTick(DispatcherQueueTimer sender, object args)
    {
        if (!_pointerInside && !_hasKeyboardFocus && _autoHide)
        {
            SetCollapsed(true);
        }
    }

    private void RefreshExpandedWidth()
    {
        var expandedDockWidth = TaskbarDockLayout.CalculateExpandedWidth(
            PinnedItems.Count,
            Items.Count,
            _monitor.WorkAreaWidth);
        if (_expandedDockWidth == expandedDockWidth)
        {
            return;
        }

        _expandedDockWidth = expandedDockWidth;
        SetCollapsed(_collapsed);
    }

    private void RefreshPinnedItems()
    {
        var visibleApplications = _pinnedApplications
            .Where(application => !_monitorWindows.Any(
                window => TaskbarPinWindowMatcher.IsMatch(application, window)))
            .ToArray();
        if (!visibleApplications
            .Select(static command => command.Id)
            .SequenceEqual(
                PinnedItems.Select(static item => item.Id),
                StringComparer.OrdinalIgnoreCase))
        {
            PinnedItems.Clear();
            foreach (var application in visibleApplications)
            {
                PinnedItems.Add(new PinnedDockItemViewModel(application));
            }
        }

        PinnedSeparator.Visibility =
            PinnedItems.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        RefreshExpandedWidth();
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
            _ = _windowService.ToggleTaskbarWindow(item.Handle);
            ScheduleAutoHide();
        }
    }

    private void OnWindowContextRequested(
        UIElement sender,
        ContextRequestedEventArgs args)
    {
        if (sender is not FrameworkElement element ||
            element.DataContext is not DockItemViewModel item)
        {
            return;
        }

        _autoHideTimer.Stop();
        _contextMenuOpen = true;

        var toggleAction =
            TaskbarWindowActionResolver.ResolveContextToggle(item.IsMinimized);
        var toggleItem = new MenuFlyoutItem
        {
            Text = toggleAction == TaskbarWindowAction.Minimize
                ? "Minimize"
                : "Restore",
            Icon = new FontIcon
            {
                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily(
                    "Segoe Fluent Icons"),
                Glyph = toggleAction == TaskbarWindowAction.Minimize
                    ? "\uE921"
                    : "\uE923",
            },
        };
        toggleItem.Click += (_, _) =>
        {
            _ = toggleAction == TaskbarWindowAction.Minimize
                ? _windowService.Minimize(item.Handle)
                : _windowService.RestoreAndActivate(item.Handle);
        };

        var closeItem = new MenuFlyoutItem
        {
            Text = "Close window",
            Icon = new FontIcon
            {
                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily(
                    "Segoe Fluent Icons"),
                Glyph = "\uE8BB",
            },
        };
        closeItem.Click += (_, _) => _ = _windowService.RequestClose(item.Handle);

        var flyout = new MenuFlyout();
        flyout.Items.Add(toggleItem);
        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(closeItem);
        flyout.Closed += (_, _) =>
        {
            _contextMenuOpen = false;
            ScheduleAutoHide();
        };
        flyout.ShowAt(element);
        args.Handled = true;
    }

    private void OnLauncherClicked(object sender, RoutedEventArgs e)
    {
        LauncherRequested?.Invoke(this, EventArgs.Empty);
        ScheduleAutoHide();
    }

    private void OnSystemAreaClicked(object sender, RoutedEventArgs e)
    {
        SystemAreaRequested?.Invoke(this, EventArgs.Empty);
        ScheduleAutoHide();
    }

    private async void OnPinnedApplicationClicked(
        object sender,
        ItemClickEventArgs e)
    {
        if (e.ClickedItem is not PinnedDockItemViewModel item)
        {
            return;
        }

        try
        {
            await item.Command.ExecuteAsync(CancellationToken.None)
                .ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            DockCountText.Text = "Launch failed";
            ToolTipService.SetToolTip(
                DockCountText,
                $"Unable to open {item.Title}: {exception.Message}");
        }
        finally
        {
            ScheduleAutoHide();
        }
    }

    private void OnShellStateChanged(object? sender, ShellState state)
    {
        if (state.Mode == ShellMode.Gaming)
        {
            _autoHideTimer.Stop();
            AppWindow.Hide();
            return;
        }

        SetCollapsed(false);
        AppWindow.Show();
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
