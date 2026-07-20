namespace SeanShell.Plugins;

public sealed record PluginStateChangeResult(
    bool Success,
    PluginDiagnostic Diagnostic);
