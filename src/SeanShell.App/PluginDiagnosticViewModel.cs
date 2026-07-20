using SeanShell.Plugins;

namespace SeanShell.App;

public sealed class PluginDiagnosticViewModel
{
    public PluginDiagnosticViewModel(PluginDiagnostic diagnostic, bool canToggle)
    {
        Id = diagnostic.Id;
        Name = $"{diagnostic.Name} {diagnostic.Version}";
        Identity = $"{diagnostic.Publisher} · {diagnostic.Id}";
        IsEnabled = diagnostic.IsEnabled;
        CanToggle = canToggle;
        ToggleName = $"Enable {diagnostic.Name}";
        State = diagnostic.State.ToString();
        CapabilityText = diagnostic.Capabilities.ToString();
        Detail = diagnostic.LastError is null
            ? $"Last operation: {diagnostic.LastOperation} ({diagnostic.LastDuration.TotalMilliseconds:F0} ms)"
            : $"{diagnostic.LastOperation} failed: {diagnostic.LastError}";
    }

    public string Id { get; }

    public string Name { get; }

    public string Identity { get; }

    public bool IsEnabled { get; }

    public bool CanToggle { get; }

    public string ToggleName { get; }

    public string State { get; }

    public string CapabilityText { get; }

    public string Detail { get; }
}
