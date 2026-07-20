using SeanShell.Plugins;

namespace SeanShell.App;

public sealed class PluginDiagnosticViewModel
{
    public PluginDiagnosticViewModel(PluginDiagnostic diagnostic)
    {
        Name = $"{diagnostic.Name} {diagnostic.Version}";
        Identity = $"{diagnostic.Publisher} · {diagnostic.Id}";
        State = diagnostic.State.ToString();
        CapabilityText = diagnostic.Capabilities.ToString();
        Detail = diagnostic.LastError is null
            ? $"Last operation: {diagnostic.LastOperation} ({diagnostic.LastDuration.TotalMilliseconds:F0} ms)"
            : $"{diagnostic.LastOperation} failed: {diagnostic.LastError}";
    }

    public string Name { get; }

    public string Identity { get; }

    public string State { get; }

    public string CapabilityText { get; }

    public string Detail { get; }
}
