using SeanShell.PluginContracts;

namespace SeanShell.Plugins;

public sealed record PluginRegistration(PluginManifest Manifest, ISeanShellPlugin Instance);
