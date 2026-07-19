using System.Collections.ObjectModel;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SeanShell.Core;
using Windows.Graphics;
using Windows.System;

namespace SeanShell.App;

public sealed partial class LauncherWindow : Window
{
    private const int WindowWidth = 720;
    private const int WindowHeight = 560;
    private readonly LauncherSearchService _searchService;
    private CancellationTokenSource? _searchCancellation;
    private bool _allowClose;

    public LauncherWindow(LauncherSearchService searchService)
    {
        _searchService = searchService;
        InitializeComponent();

        ResultsList.ItemsSource = Results;
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(LauncherTitleBar);
        AppWindow.SetIcon("Assets/AppIcon.ico");
        ConfigurePresenter();

        AppWindow.Closing += OnWindowClosing;
    }

    public ObservableCollection<LauncherResultViewModel> Results { get; } = [];

    public async Task ShowLauncherAsync()
    {
        CenterOnCurrentDisplay();
        AppWindow.Show();
        Activate();

        SearchBox.Text = string.Empty;
        SearchBox.Focus(FocusState.Programmatic);
        SearchBox.SelectAll();
        await RefreshResultsAsync(string.Empty).ConfigureAwait(true);
    }

    public void HideLauncher()
    {
        _searchCancellation?.Cancel();
        AppWindow.Hide();
    }

    public void Shutdown()
    {
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
        AppWindow.SetPresenter(presenter);
        AppWindow.Resize(new SizeInt32(WindowWidth, WindowHeight));
    }

    private void CenterOnCurrentDisplay()
    {
        var displayArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        var workArea = displayArea.WorkArea;
        var x = workArea.X + Math.Max(0, (workArea.Width - WindowWidth) / 2);
        var y = workArea.Y + Math.Max(0, (workArea.Height - WindowHeight) / 3);
        AppWindow.Move(new PointInt32(x, y));
    }

    private async void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        _searchCancellation = new CancellationTokenSource();

        try
        {
            await Task.Delay(60, _searchCancellation.Token).ConfigureAwait(true);
            await RefreshResultsAsync(SearchBox.Text, _searchCancellation.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task RefreshResultsAsync(string query, CancellationToken cancellationToken = default)
    {
        SearchProgress.IsActive = true;
        EmptyState.Visibility = Visibility.Collapsed;

        try
        {
            var commands = await _searchService.SearchAsync(query, 8, cancellationToken).ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();

            Results.Clear();
            foreach (var command in commands)
            {
                Results.Add(new LauncherResultViewModel(command));
            }

            ResultsList.SelectedIndex = Results.Count > 0 ? 0 : -1;
            EmptyState.Visibility = Results.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            ResultStatus.Text = Results.Count == 1 ? "1 result" : $"{Results.Count} results";
        }
        finally
        {
            SearchProgress.IsActive = false;
        }
    }

    private async void OnResultClicked(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is LauncherResultViewModel result)
        {
            await ExecuteAsync(result).ConfigureAwait(true);
        }
    }

    private async void OnSearchBoxKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Down:
                MoveSelection(1);
                e.Handled = true;
                break;
            case VirtualKey.Up:
                MoveSelection(-1);
                e.Handled = true;
                break;
            case VirtualKey.Enter when ResultsList.SelectedItem is LauncherResultViewModel result:
                e.Handled = true;
                await ExecuteAsync(result).ConfigureAwait(true);
                break;
            case VirtualKey.Escape:
                e.Handled = true;
                HideLauncher();
                break;
        }
    }

    private void OnWindowKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            HideLauncher();
        }
    }

    private void MoveSelection(int delta)
    {
        if (Results.Count == 0)
        {
            return;
        }

        var current = Math.Max(0, ResultsList.SelectedIndex);
        ResultsList.SelectedIndex = Math.Clamp(current + delta, 0, Results.Count - 1);
        ResultsList.ScrollIntoView(ResultsList.SelectedItem);
    }

    private async Task ExecuteAsync(LauncherResultViewModel result)
    {
        try
        {
            ErrorInfoBar.IsOpen = false;
            await result.Command.ExecuteAsync(CancellationToken.None).ConfigureAwait(true);
            HideLauncher();
        }
        catch (Exception exception)
        {
            ErrorInfoBar.Message = exception.Message;
            ErrorInfoBar.IsOpen = true;
        }
    }

    private void OnWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_allowClose)
        {
            return;
        }

        args.Cancel = true;
        HideLauncher();
    }
}
