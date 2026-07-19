using System.Collections.ObjectModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SeanShell.Core;
using SeanShell.Windows;
using Windows.Graphics;

namespace SeanShell.App;

public sealed partial class DockWindow : Window
{
    private const int DockWidth = 760;
    private const int DockHeight = 80;
    private const int EdgeOffset = 12;
    private readonly DesktopWindowService _windowService;
    private readonly ShellStateStore _shellState;
    private readonly DispatcherQueueTimer _refreshTimer;
    private bool _allowClose;
    private bool _refreshing;

    public DockWindow(DesktopWindowService windowService, ShellStateStore shellState)
    {
        _windowService = windowService;
        _shellState = shellState;
        InitializeComponent();

        WindowList.ItemsSource = Items;
        AppWindow.SetIcon("Assets/AppIcon.ico");
        ConfigurePresenter();

        _refreshTimer = DispatcherQueue.CreateTimer();
        _refreshTimer.Interval = TimeSpan.FromSeconds(2);
        _refreshTimer.Tick += OnRefreshTimerTick;
        _shellState.StateChanged += OnShellStateChanged;
        AppWindow.Closing += OnWindowClosing;
    }

    public ObservableCollection<DockItemViewModel> Items { get; } = [];

    public void ShowDock()
    {
        PositionDock();
        EmptyState.Visibility = Visibility.Visible;
        AppWindow.Show();
        _refreshTimer.Start();
        _ = RefreshWindowsAsync();
    }

    public void Shutdown()
    {
        _refreshTimer.Stop();
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
        AppWindow.Resize(new SizeInt32(DockWidth, DockHeight));
    }

    private void PositionDock()
    {
        var displayArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        var workArea = displayArea.WorkArea;
        var x = workArea.X + Math.Max(0, (workArea.Width - DockWidth) / 2);
        var y = workArea.Y + Math.Max(0, workArea.Height - DockHeight - EdgeOffset);
        AppWindow.Move(new PointInt32(x, y));
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
            var windows = await Task.Run(() => _windowService.Capture().Take(12).ToArray())
                .ConfigureAwait(true);
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

            EmptyStateText.Text = "No open application windows";
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

    private void OnWindowClicked(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is DockItemViewModel item)
        {
            _ = _windowService.Activate(item.Handle);
        }
    }

    private void OnShellStateChanged(object? sender, ShellState state)
    {
        if (state.Mode == ShellMode.Gaming)
        {
            _refreshTimer.Stop();
            AppWindow.Hide();
            return;
        }

        PositionDock();
        AppWindow.Show();
        _refreshTimer.Start();
        _ = RefreshWindowsAsync();
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
