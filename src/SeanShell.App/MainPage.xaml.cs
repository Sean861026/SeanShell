using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
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

    private void OnGamingModeToggled(object sender, RoutedEventArgs e)
    {
        _shellState.SetMode(GamingModeToggle.IsOn ? ShellMode.Gaming : ShellMode.Normal);
    }

    private void OnShellStateChanged(object? sender, ShellState state)
    {
        ModeText.Text = state.Mode == ShellMode.Gaming ? "Gaming" : "Normal";
    }
}
