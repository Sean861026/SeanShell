using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using SeanShell.Core;

namespace SeanShell.App;

public sealed class PinnedDockItemViewModel(ShellCommand command) : INotifyPropertyChanged
{
    private ImageSource? _icon;

    public ShellCommand Command { get; } = command;

    public string Id => Command.Id;

    public string Title => Command.Title;

    public string Glyph => Command.Glyph;

    public ImageSource? Icon => _icon;

    public Visibility IconVisibility =>
        Icon is null ? Visibility.Collapsed : Visibility.Visible;

    public Visibility FallbackIconVisibility =>
        Icon is null ? Visibility.Visible : Visibility.Collapsed;

    public string ToolTipText => $"{Title}\nPinned · Drag to reorder";

    public string ToolTipMetaText => "Pinned application";

    public string ToolTipActionText => "Click to open • Drag to reorder";

    public string AccessibleName => $"Open pinned application {Title}";

    public event PropertyChangedEventHandler? PropertyChanged;

    public async Task LoadIconAsync()
    {
        var icon = await ApplicationIconSourceCache.GetAsync(Command.Icon);
        if (icon is null || ReferenceEquals(_icon, icon))
        {
            return;
        }

        _icon = icon;
        OnPropertyChanged(nameof(Icon));
        OnPropertyChanged(nameof(IconVisibility));
        OnPropertyChanged(nameof(FallbackIconVisibility));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
