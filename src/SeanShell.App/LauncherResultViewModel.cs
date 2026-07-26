using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using SeanShell.Core;

namespace SeanShell.App;

public sealed class LauncherResultViewModel(
    ShellCommand command,
    bool isPinned = false) : INotifyPropertyChanged
{
    private bool _isPinned = isPinned;

    public ShellCommand Command { get; } = command;

    public string Title => Command.Title;

    public string Subtitle => Command.Subtitle ?? string.Empty;

    public string Glyph => Command.Glyph;

    public bool CanPin => Command.Kind == ShellCommandKind.Application;

    public Visibility PinVisibility =>
        CanPin ? Visibility.Visible : Visibility.Collapsed;

    public bool IsPinned => _isPinned;

    public string PinGlyph => IsPinned ? "\uE77A" : "\uE718";

    public string PinLabel =>
        IsPinned ? $"Unpin {Title} from Dock" : $"Pin {Title} to Dock";

    public string KindLabel => Command.Kind switch
    {
        ShellCommandKind.Application => "App",
        ShellCommandKind.System => "System",
        _ => "Plugin",
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    public void SetPinned(bool isPinned)
    {
        if (_isPinned == isPinned)
        {
            return;
        }

        _isPinned = isPinned;
        OnPropertyChanged(nameof(IsPinned));
        OnPropertyChanged(nameof(PinGlyph));
        OnPropertyChanged(nameof(PinLabel));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
