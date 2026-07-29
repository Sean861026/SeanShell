using System.Collections.ObjectModel;
using System.Globalization;
using System.Numerics;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using SeanShell.Core;
using SeanShell.Windows;
using Windows.ApplicationModel.DataTransfer;
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
    private bool _reducedEffects;
    private int _expandedDockWidth;
    private IReadOnlyList<ShellCommand> _availableApplications = [];
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
            _monitor.WorkAreaWidth,
            _textScaleFactor);
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

    public event Func<ShellCommand, bool, Task<bool>>? PinChangedRequested;

    public event Func<ShellCommand, PinnedApplicationMoveDirection, Task<bool>>?
        PinMoveRequested;

    public event Func<IReadOnlyList<string>, Task<bool>>? PinOrderRequested;

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
        RefreshExpandedWidth();
        SetCollapsed(_collapsed);
    }

    public void SetReducedEffects(bool enabled)
    {
        _reducedEffects = enabled;
        SystemBackdrop = enabled
            ? null
            : new MicaBackdrop { Kind = MicaKind.BaseAlt };
        ExpandedDock.Background = Application.Current.Resources[
            enabled
                ? "CardBackgroundFillColorDefaultBrush"
                : "LayerFillColorAltBrush"] as Brush;
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
        if (windows.Count == _monitorWindows.Count && windows
            .Select(static window => (
                window.Handle,
                window.Title,
                window.ProcessName,
                window.IsMinimized,
                window.IsForeground))
            .SequenceEqual(_monitorWindows.Select(static window => (
                window.Handle,
                window.Title,
                window.ProcessName,
                window.IsMinimized,
                window.IsForeground))))
        {
            return;
        }

        _monitorWindows = windows;
        RefreshWindowItems();
        DockCountText.Text = windows.Count == 1 ? "1 window" : $"{windows.Count} windows";
        EmptyStateText.Text = $"No open application windows on {_monitor.DeviceName}";
        EmptyState.Visibility = Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RefreshPinnedItems();
    }

    private void RefreshWindowItems()
    {
        Items.Clear();
        foreach (var group in TaskbarWindowGrouper.Group(_monitorWindows))
        {
            var isPinned = TaskbarDockPinResolver.FindPinnedApplication(
                _pinnedApplications,
                group.Windows) is not null;
            Items.Add(new DockItemViewModel(group, isPinned));
        }

        WindowList.SelectedItem = Items.FirstOrDefault(static item => item.IsForeground);
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
        RefreshWindowItems();
        RefreshPinnedItems();
    }

    public void ApplyAvailableApplications(
        IReadOnlyList<ShellCommand> applications)
    {
        if (_allowClose)
        {
            return;
        }

        _availableApplications = applications.ToArray();
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
            _monitor.WorkAreaWidth,
            _textScaleFactor);
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

    private void OnDockItemPointerEntered(
        object sender,
        PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            ApplyDockItemMotion(element, isPointerOver: true, isPressed: false);
        }
    }

    private void OnDockItemPointerExited(
        object sender,
        PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            ApplyDockItemMotion(element, isPointerOver: false, isPressed: false);
        }
    }

    private void OnDockItemPointerPressed(
        object sender,
        PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement element &&
            e.GetCurrentPoint(element).Properties.IsLeftButtonPressed)
        {
            ApplyDockItemMotion(element, isPointerOver: true, isPressed: true);
        }
    }

    private void OnDockItemPointerReleased(
        object sender,
        PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            ApplyDockItemMotion(element, isPointerOver: true, isPressed: false);
        }
    }

    private void ApplyDockItemMotion(
        FrameworkElement element,
        bool isPointerOver,
        bool isPressed)
    {
        var motion = DockItemMotion.Resolve(
            isPointerOver,
            isPressed,
            _reducedEffects);
        element.CenterPoint = new Vector3(
            (float)(element.ActualWidth / 2),
            (float)(element.ActualHeight / 2),
            0);
        var duration = TimeSpan.FromMilliseconds(motion.DurationMilliseconds);
        element.ScaleTransition = motion.DurationMilliseconds == 0
            ? null
            : new Vector3Transition { Duration = duration };
        element.TranslationTransition = motion.DurationMilliseconds == 0
            ? null
            : new Vector3Transition { Duration = duration };
        element.Scale = new Vector3(motion.Scale, motion.Scale, 1);
        element.Translation = new Vector3(0, motion.TranslationY, 0);
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
        if (e.ClickedItem is not DockItemViewModel item)
        {
            return;
        }

        if (item.WindowCount == 1)
        {
            _ = _windowService.ToggleTaskbarWindow(item.PrimaryWindow.Handle);
            ScheduleAutoHide();
            return;
        }

        if (WindowList.ContainerFromItem(item) is FrameworkElement anchor)
        {
            ShowWindowPicker(item, anchor);
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

        if (item.WindowCount > 1)
        {
            ShowGroupWindowContextMenu(item, element);
            args.Handled = true;
            return;
        }

        var window = item.PrimaryWindow;
        _autoHideTimer.Stop();
        _contextMenuOpen = true;

        var toggleAction =
            TaskbarWindowActionResolver.ResolveContextToggle(window.IsMinimized);
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
                ? _windowService.Minimize(window.Handle)
                : _windowService.RestoreAndActivate(window.Handle);
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
        closeItem.Click += (_, _) => _ = _windowService.RequestClose(window.Handle);

        var flyout = new MenuFlyout();
        flyout.Items.Add(toggleItem);
        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(closeItem);
        AddOpenNewInstanceAction(flyout.Items, item);
        AddPinAction(flyout.Items, item);
        flyout.Closed += (_, _) =>
        {
            _contextMenuOpen = false;
            ScheduleAutoHide();
        };
        flyout.ShowAt(element);
        args.Handled = true;
    }

    private void ShowGroupWindowContextMenu(
        DockItemViewModel item,
        FrameworkElement anchor)
    {
        _autoHideTimer.Stop();
        _contextMenuOpen = true;

        var flyout = new MenuFlyout();
        for (var index = 0; index < item.Windows.Count; index++)
        {
            var window = item.Windows[index];
            var windowMenu = new MenuFlyoutSubItem
            {
                Text = GetWindowDisplayTitle(item, index),
                Icon = CreateWindowStateIcon(window),
            };

            var activateItem = new MenuFlyoutItem
            {
                Text = "Activate",
                Icon = new FontIcon
                {
                    FontFamily = new Microsoft.UI.Xaml.Media.FontFamily(
                        "Segoe Fluent Icons"),
                    Glyph = "\uE8A7",
                },
            };
            activateItem.Click += (_, _) =>
                _ = _windowService.RestoreAndActivate(window.Handle);
            windowMenu.Items.Add(activateItem);

            var toggleAction =
                TaskbarWindowActionResolver.ResolveContextToggle(window.IsMinimized);
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
                    ? _windowService.Minimize(window.Handle)
                    : _windowService.RestoreAndActivate(window.Handle);
            };
            windowMenu.Items.Add(toggleItem);
            windowMenu.Items.Add(new MenuFlyoutSeparator());

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
            closeItem.Click += (_, _) =>
                _ = _windowService.RequestClose(window.Handle);
            windowMenu.Items.Add(closeItem);
            flyout.Items.Add(windowMenu);
        }

        AddOpenNewInstanceAction(flyout.Items, item);
        AddPinAction(flyout.Items, item);
        flyout.Closed += (_, _) =>
        {
            _contextMenuOpen = false;
            ScheduleAutoHide();
        };
        flyout.ShowAt(anchor);
    }

    private void ShowWindowPicker(
        DockItemViewModel item,
        FrameworkElement anchor)
    {
        _autoHideTimer.Stop();
        _contextMenuOpen = true;

        var flyout = new MenuFlyout();
        for (var index = 0; index < item.Windows.Count; index++)
        {
            var window = item.Windows[index];
            var state = window.IsForeground
                ? "Active"
                : window.IsMinimized
                    ? "Minimized"
                    : "Running";
            var windowItem = new MenuFlyoutItem
            {
                Text = $"{GetWindowDisplayTitle(item, index)} — {state}",
                Icon = CreateWindowStateIcon(window),
            };
            windowItem.Click += (_, _) =>
                _ = _windowService.RestoreAndActivate(window.Handle);
            flyout.Items.Add(windowItem);
        }

        flyout.Closed += (_, _) =>
        {
            _contextMenuOpen = false;
            ScheduleAutoHide();
        };
        flyout.ShowAt(anchor);
    }

    private static string GetWindowDisplayTitle(
        DockItemViewModel item,
        int index)
    {
        var window = item.Windows[index];
        var matchingTitleCount = item.Windows.Count(candidate =>
            candidate.Title.Equals(
                window.Title,
                StringComparison.CurrentCultureIgnoreCase));
        if (matchingTitleCount == 1)
        {
            return window.Title;
        }

        var matchingTitleIndex = item.Windows
            .Take(index + 1)
            .Count(candidate => candidate.Title.Equals(
                window.Title,
                StringComparison.CurrentCultureIgnoreCase));
        return $"{window.Title} ({matchingTitleIndex} of {matchingTitleCount})";
    }

    private static FontIcon CreateWindowStateIcon(
        DesktopWindowSnapshot window) =>
        new()
        {
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily(
                "Segoe Fluent Icons"),
            Glyph = window.IsForeground ? "\uE73E" : "\uE8A7",
        };

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

    private async void OnPinnedApplicationsDragCompleted(
        ListViewBase sender,
        DragItemsCompletedEventArgs args)
    {
        if (args.DropResult != DataPackageOperation.Move ||
            PinOrderRequested is not { } handler)
        {
            return;
        }

        try
        {
            var visibleOrder = PinnedItems
                .Select(static item => item.Id)
                .ToArray();
            if (!await handler(visibleOrder).ConfigureAwait(true))
            {
                RefreshPinnedItems();
            }
        }
        catch (Exception exception)
        {
            DockCountText.Text = "Reorder failed";
            ToolTipService.SetToolTip(DockCountText, exception.Message);
            RefreshPinnedItems();
        }
    }

    private void OnPinnedApplicationContextRequested(
        UIElement sender,
        ContextRequestedEventArgs args)
    {
        if (sender is not FrameworkElement element ||
            element.DataContext is not PinnedDockItemViewModel item)
        {
            return;
        }

        _autoHideTimer.Stop();
        _contextMenuOpen = true;

        var flyout = new MenuFlyout();
        AddPinnedApplicationActions(
            flyout.Items,
            item.Command,
            includeLeadingSeparator: false);
        flyout.Closed += (_, _) =>
        {
            _contextMenuOpen = false;
            ScheduleAutoHide();
        };
        flyout.ShowAt(element);
        args.Handled = true;
    }

    private void AddPinAction(
        IList<MenuFlyoutItemBase> items,
        DockItemViewModel dockItem)
    {
        var pinnedApplication = TaskbarDockPinResolver.FindPinnedApplication(
            _pinnedApplications,
            dockItem.Windows);
        if (pinnedApplication is not null)
        {
            AddPinnedApplicationActions(
                items,
                pinnedApplication,
                includeLeadingSeparator: true);
            return;
        }

        var candidates = TaskbarDockPinResolver.FindPinCandidates(
            _availableApplications,
            dockItem.Windows);
        if (candidates.Count == 0)
        {
            return;
        }

        items.Add(new MenuFlyoutSeparator());
        if (candidates.Count == 1)
        {
            items.Add(CreatePinMenuItem(candidates[0], shouldPin: true));
            return;
        }

        var pinMenu = new MenuFlyoutSubItem
        {
            Text = "Pin to Dock",
            Icon = CreatePinIcon(shouldPin: true),
        };
        foreach (var candidate in candidates)
        {
            var candidateItem = new MenuFlyoutItem
            {
                Text = candidate.Title,
            };
            candidateItem.Click += async (_, _) =>
                await RequestPinChangeAsync(candidate, shouldPin: true)
                    .ConfigureAwait(true);
            pinMenu.Items.Add(candidateItem);
        }

        items.Add(pinMenu);
    }

    private void AddOpenNewInstanceAction(
        IList<MenuFlyoutItemBase> items,
        DockItemViewModel dockItem)
    {
        var candidates = TaskbarDockPinResolver.FindApplicationCandidates(
            _availableApplications,
            dockItem.Windows);
        if (candidates.Count == 0)
        {
            return;
        }

        items.Add(new MenuFlyoutSeparator());
        if (candidates.Count == 1)
        {
            items.Add(CreateOpenNewInstanceMenuItem(candidates[0]));
            return;
        }

        var openMenu = new MenuFlyoutSubItem
        {
            Text = "Open new instance",
            Icon = CreateOpenNewInstanceIcon(),
        };
        foreach (var candidate in candidates)
        {
            var candidateItem = new MenuFlyoutItem
            {
                Text = candidate.Title,
            };
            candidateItem.Click += async (_, _) =>
                await OpenNewInstanceAsync(candidate).ConfigureAwait(true);
            openMenu.Items.Add(candidateItem);
        }

        items.Add(openMenu);
    }

    private MenuFlyoutItem CreateOpenNewInstanceMenuItem(
        ShellCommand application)
    {
        var item = new MenuFlyoutItem
        {
            Text = "Open new instance",
            Icon = CreateOpenNewInstanceIcon(),
        };
        item.Click += async (_, _) =>
            await OpenNewInstanceAsync(application).ConfigureAwait(true);
        return item;
    }

    private static FontIcon CreateOpenNewInstanceIcon() =>
        new()
        {
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily(
                "Segoe Fluent Icons"),
            Glyph = "\uE8A7",
        };

    private async Task OpenNewInstanceAsync(ShellCommand application)
    {
        try
        {
            await application.ExecuteAsync(CancellationToken.None)
                .ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            DockCountText.Text = "Launch failed";
            ToolTipService.SetToolTip(
                DockCountText,
                $"Unable to open {application.Title}: {exception.Message}");
        }
    }

    private void AddPinnedApplicationActions(
        IList<MenuFlyoutItemBase> items,
        ShellCommand application,
        bool includeLeadingSeparator)
    {
        if (includeLeadingSeparator)
        {
            items.Add(new MenuFlyoutSeparator());
        }

        items.Add(CreatePinMenuItem(application, shouldPin: false));
        items.Add(new MenuFlyoutSeparator());
        items.Add(CreateMoveMenuItem(
            application,
            PinnedApplicationMoveDirection.Left));
        items.Add(CreateMoveMenuItem(
            application,
            PinnedApplicationMoveDirection.Right));
    }

    private MenuFlyoutItem CreatePinMenuItem(
        ShellCommand command,
        bool shouldPin)
    {
        var item = new MenuFlyoutItem
        {
            Text = shouldPin ? "Pin to Dock" : "Unpin from Dock",
            Icon = CreatePinIcon(shouldPin),
        };
        item.Click += async (_, _) =>
            await RequestPinChangeAsync(command, shouldPin).ConfigureAwait(true);
        return item;
    }

    private static FontIcon CreatePinIcon(bool shouldPin) =>
        new()
        {
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily(
                "Segoe Fluent Icons"),
            Glyph = shouldPin ? "\uE718" : "\uE77A",
        };

    private MenuFlyoutItem CreateMoveMenuItem(
        ShellCommand application,
        PinnedApplicationMoveDirection direction)
    {
        var applicationIds = _pinnedApplications
            .Select(static command => command.Id)
            .ToArray();
        var isLeft = direction == PinnedApplicationMoveDirection.Left;
        var item = new MenuFlyoutItem
        {
            Text = isLeft ? "Move left" : "Move right",
            Icon = new FontIcon
            {
                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily(
                    "Segoe Fluent Icons"),
                Glyph = isLeft ? "\uE76B" : "\uE76C",
            },
            IsEnabled = PinnedApplicationOrder.CanMove(
                applicationIds,
                application.Id,
                direction),
        };
        item.Click += async (_, _) =>
            await RequestPinMoveAsync(application, direction).ConfigureAwait(true);
        return item;
    }

    private async Task RequestPinChangeAsync(
        ShellCommand command,
        bool shouldPin)
    {
        var handler = PinChangedRequested;
        if (handler is null)
        {
            return;
        }

        try
        {
            if (!await handler(command, shouldPin).ConfigureAwait(true))
            {
                throw new InvalidOperationException(
                    "The pinned application preference was not changed.");
            }
        }
        catch (Exception exception)
        {
            DockCountText.Text = shouldPin ? "Pin failed" : "Unpin failed";
            ToolTipService.SetToolTip(DockCountText, exception.Message);
        }
    }

    private async Task RequestPinMoveAsync(
        ShellCommand application,
        PinnedApplicationMoveDirection direction)
    {
        var handler = PinMoveRequested;
        if (handler is null)
        {
            return;
        }

        try
        {
            if (!await handler(application, direction).ConfigureAwait(true))
            {
                throw new InvalidOperationException(
                    "The pinned application order was not changed.");
            }
        }
        catch (Exception exception)
        {
            DockCountText.Text = "Move failed";
            ToolTipService.SetToolTip(DockCountText, exception.Message);
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
