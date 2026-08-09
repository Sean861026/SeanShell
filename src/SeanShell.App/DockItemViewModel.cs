using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using SeanShell.Core;

namespace SeanShell.App;

public sealed class DockItemViewModel(
    TaskbarWindowGroup group,
    bool isPinned = false) : INotifyPropertyChanged
{
    private readonly ApplicationIconSnapshot? _iconSnapshot =
        group.PrimaryWindow.Icon ??
        group.Windows.FirstOrDefault(static window => window.Icon is not null)?.Icon;
    private readonly TaskbarItemVisualState _visualState =
        TaskbarItemVisualStateResolver.Resolve(
            group.IsForeground,
            group.IsMinimized);
    private ImageSource? _icon;
    private string? _interactionNotice;

    public IReadOnlyList<DesktopWindowSnapshot> Windows { get; } = group.Windows;

    public DesktopWindowSnapshot PrimaryWindow { get; } = group.PrimaryWindow;

    public string ProcessName { get; } = group.ProcessName;

    public string Title =>
        WindowCount == 1 ? PrimaryWindow.Title : ProcessName;

    public bool IsForeground { get; } = group.IsForeground;

    public bool IsMinimized { get; } = group.IsMinimized;

    public bool IsPinned { get; } = isPinned;

    public int WindowCount => Windows.Count;

    public ImageSource? Icon => _icon;

    public Visibility IconVisibility =>
        Icon is null ? Visibility.Collapsed : Visibility.Visible;

    public Visibility FallbackIconVisibility =>
        Icon is null ? Visibility.Visible : Visibility.Collapsed;

    public double ContentOpacity => _visualState.ContentOpacity;

    public Visibility ActiveIndicatorVisibility =>
        _visualState.Indicator == TaskbarItemIndicator.Active
            ? Visibility.Visible
            : Visibility.Collapsed;

    public Visibility RunningIndicatorVisibility =>
        _visualState.Indicator == TaskbarItemIndicator.Running
            ? Visibility.Visible
            : Visibility.Collapsed;

    public Visibility MinimizedIndicatorVisibility =>
        _visualState.Indicator == TaskbarItemIndicator.Minimized
            ? Visibility.Visible
            : Visibility.Collapsed;

    public Visibility PinnedIndicatorVisibility =>
        IsPinned ? Visibility.Visible : Visibility.Collapsed;

    public Visibility CountBadgeVisibility =>
        WindowCount > 1 ? Visibility.Visible : Visibility.Collapsed;

    public string CountText => WindowCount.ToString(
        System.Globalization.CultureInfo.CurrentCulture);

    public string StateText => IsForeground
        ? "Active"
        : IsMinimized
            ? "Minimized"
            : "Running";

    public string ToolTipMetaText => WindowCount > 1
        ? $"{ProcessName} • {WindowCount} windows • {StateText}"
        : $"{ProcessName} • {StateText}";

    public string ToolTipActionText => _interactionNotice ??
        (WindowCount > 1
            ? "Click to choose • Ctrl-click to cycle"
            : "Click to switch • Shift-click for a new instance");

    public string ToolTipText
    {
        get
        {
            var title = WindowCount == 1
                ? PrimaryWindow.Title
                : $"{ProcessName} — {WindowCount} windows";
            var pinState = IsPinned ? " · Pinned" : string.Empty;
            return $"{title}\n{StateText}{pinState}";
        }
    }

    public string AccessibleName => WindowCount == 1
        ? $"Switch to {Title}, {ProcessName}, {StateText}{PinnedText}"
        : $"{ProcessName}, {WindowCount} windows, {StateText}{PinnedText}. Open window picker";

    public event PropertyChangedEventHandler? PropertyChanged;

    public void SetInteractionNotice(string notice)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(notice);
        if (string.Equals(_interactionNotice, notice, StringComparison.Ordinal))
        {
            return;
        }

        _interactionNotice = notice;
        OnPropertyChanged(nameof(ToolTipActionText));
    }

    public async Task LoadIconAsync()
    {
        var icon = await ApplicationIconSourceCache.GetAsync(_iconSnapshot);
        if (icon is null || ReferenceEquals(_icon, icon))
        {
            return;
        }

        _icon = icon;
        OnPropertyChanged(nameof(Icon));
        OnPropertyChanged(nameof(IconVisibility));
        OnPropertyChanged(nameof(FallbackIconVisibility));
    }

    private string PinnedText => IsPinned ? ", Pinned" : string.Empty;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
