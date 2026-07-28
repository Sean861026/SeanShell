using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using SeanShell.Core;

namespace SeanShell.App;

public sealed class DockItemViewModel(TaskbarWindowGroup group)
{
    private readonly TaskbarItemVisualState _visualState =
        TaskbarItemVisualStateResolver.Resolve(
            group.IsForeground,
            group.IsMinimized);

    public IReadOnlyList<DesktopWindowSnapshot> Windows { get; } = group.Windows;

    public DesktopWindowSnapshot PrimaryWindow { get; } = group.PrimaryWindow;

    public string ProcessName { get; } = group.ProcessName;

    public string Title =>
        WindowCount == 1 ? PrimaryWindow.Title : ProcessName;

    public string ToolTipText => WindowCount == 1
        ? PrimaryWindow.Title
        : $"{ProcessName} — {WindowCount} windows";

    public bool IsForeground { get; } = group.IsForeground;

    public bool IsMinimized { get; } = group.IsMinimized;

    public int WindowCount => Windows.Count;

    public ImageSource? Icon { get; } =
        ApplicationIconSourceCache.Get(
            group.PrimaryWindow.Icon ??
            group.Windows.FirstOrDefault(static window => window.Icon is not null)?.Icon);

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

    public Visibility CountBadgeVisibility =>
        WindowCount > 1 ? Visibility.Visible : Visibility.Collapsed;

    public string CountText => WindowCount.ToString(
        System.Globalization.CultureInfo.CurrentCulture);

    public string StateText => IsForeground
        ? "Active"
        : IsMinimized
            ? "Minimized"
            : "Running";

    public string AccessibleName => WindowCount == 1
        ? $"Switch to {Title}, {ProcessName}, {StateText}"
        : $"{ProcessName}, {WindowCount} windows, {StateText}. Open window picker";
}
