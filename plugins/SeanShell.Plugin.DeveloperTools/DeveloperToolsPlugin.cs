using System.Diagnostics;
using SeanShell.Core;
using SeanShell.PluginContracts;

namespace SeanShell.Plugin.DeveloperTools;

public sealed class DeveloperToolsPlugin : ISeanShellPlugin
{
    public static PluginManifest Manifest { get; } = new(
        PluginManifest.CurrentSchemaVersion,
        "seanshell.developer-tools",
        "Developer tools",
        "0.1.0",
        1,
        "SeanShell",
        PluginCapability.LauncherCommands,
        true);

    private static readonly IReadOnlyList<ShellCommand> Commands =
    [
        Create(
            "developer-settings",
            "Windows Developer Settings",
            "Configure Developer Mode and development features",
            "ms-settings:developers",
            "developer mode",
            "sideload"),
        CreateWithArguments(
            "environment-variables",
            "Environment Variables",
            "Open the Windows environment variable editor",
            "rundll32.exe",
            "sysdm.cpl,EditEnvironmentVariables",
            "path",
            "system variables"),
    ];

    public string Id => Manifest.Id;

    public string Name => Manifest.Name;

    public ValueTask InitializeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyList<ShellCommand>> GetCommandsAsync(
        string query,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Commands);
    }

    public ValueTask SuspendAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }

    public ValueTask ResumeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static ShellCommand Create(
        string id,
        string title,
        string subtitle,
        string target,
        params string[] keywords) =>
        Create(id, title, subtitle, target, null, keywords);

    private static ShellCommand CreateWithArguments(
        string id,
        string title,
        string subtitle,
        string target,
        string? arguments,
        params string[] keywords)
        => Create(id, title, subtitle, target, arguments, keywords);

    private static ShellCommand Create(
        string id,
        string title,
        string subtitle,
        string target,
        string? arguments,
        IReadOnlyList<string> keywords)
    {
        return new ShellCommand(
            $"plugin:{Manifest.Id}:{id}",
            title,
            subtitle,
            _ => LaunchAsync(target, arguments))
        {
            Kind = ShellCommandKind.Plugin,
            Glyph = "\uE943",
            Keywords = keywords,
        };
    }

    private static ValueTask LaunchAsync(string target, string? arguments)
    {
        Process.Start(new ProcessStartInfo(target)
        {
            Arguments = arguments ?? string.Empty,
            UseShellExecute = true,
        });
        return ValueTask.CompletedTask;
    }
}
