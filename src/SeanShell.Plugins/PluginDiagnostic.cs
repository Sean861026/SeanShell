using SeanShell.PluginContracts;

namespace SeanShell.Plugins;

public sealed record PluginDiagnostic(
    string Id,
    string Name,
    string Version,
    string Publisher,
    PluginCapability Capabilities,
    bool IsBuiltIn,
    bool IsEnabled,
    PluginRuntimeState State,
    string LastOperation,
    TimeSpan LastDuration,
    string? LastError);
