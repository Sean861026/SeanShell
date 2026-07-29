using SeanShell.Core;
using Windows.UI.ViewManagement;

namespace SeanShell.Windows;

public sealed class SystemAccessibilityService : IDisposable
{
    private readonly UISettings _uiSettings = new();
    private bool _disposed;

    public SystemAccessibilityService()
    {
        Current = Capture();
        _uiSettings.AnimationsEnabledChanged += OnAnimationsEnabledChanged;
        _uiSettings.ColorValuesChanged += OnColorValuesChanged;
        _uiSettings.TextScaleFactorChanged += OnTextScaleFactorChanged;
    }

    public event EventHandler<SystemAccessibilitySnapshot>? Changed;

    public SystemAccessibilitySnapshot Current { get; private set; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _uiSettings.AnimationsEnabledChanged -= OnAnimationsEnabledChanged;
        _uiSettings.ColorValuesChanged -= OnColorValuesChanged;
        _uiSettings.TextScaleFactorChanged -= OnTextScaleFactorChanged;
        _disposed = true;
    }

    private SystemAccessibilitySnapshot Capture() => new(
        _uiSettings.AnimationsEnabled,
        _uiSettings.TextScaleFactor,
        HighContrastReader.IsEnabled());

    private void Refresh()
    {
        var current = Capture();
        if (current == Current)
        {
            return;
        }

        Current = current;
        Changed?.Invoke(this, current);
    }

    private void OnAnimationsEnabledChanged(UISettings sender, UISettingsAnimationsEnabledChangedEventArgs args) =>
        Refresh();

    private void OnColorValuesChanged(UISettings sender, object args) => Refresh();

    private void OnTextScaleFactorChanged(UISettings sender, object args) => Refresh();
}
