using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SeanShell.Core;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace SeanShell.App;

/// <summary>
/// The main content page displayed inside the application window.
/// Add your UI logic, event handlers, and data binding here.
/// </summary>
public sealed partial class MainPage : Page
{
    private readonly ShellStateStore _shellState = new();

    public MainPage()
    {
        InitializeComponent();
        _shellState.StateChanged += OnShellStateChanged;
    }

    public event EventHandler? LauncherRequested;

    public void SetShortcutUnavailable(string reason)
    {
        ShortcutStatus.Text = $"Alt + Space is unavailable. Use the button instead. {reason}";
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
        ModeText.Text = state.Mode == ShellMode.Gaming ? "Gaming" : "Normal";
    }
}
