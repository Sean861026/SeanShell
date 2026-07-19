using System.Diagnostics;
using SeanShell.Core;

namespace SeanShell.Windows;

public sealed class SystemCommandProvider : ILauncherCommandProvider
{
    private static readonly IReadOnlyList<ShellCommand> Commands =
    [
        Create("settings", "Windows Settings", "Open Windows Settings", "ms-settings:", "\uE713", "preferences", "control panel"),
        Create("explorer", "File Explorer", "Browse files and folders", "explorer.exe", "\uEC50", "files", "folders"),
        Create("terminal", "Windows Terminal", "Open a terminal window", "wt.exe", "\uE756", "console", "powershell", "cmd"),
        Create("task-manager", "Task Manager", "Inspect running processes", "taskmgr.exe", "\uE9D9", "processes", "performance"),
        Create("wsl", "Windows Subsystem for Linux", "Open the default WSL distribution", "wsl.exe", "\uE756", "linux", "shell"),
    ];

    public ValueTask<IReadOnlyList<ShellCommand>> GetCommandsAsync(
        string query,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Commands);
    }

    private static ShellCommand Create(
        string id,
        string title,
        string subtitle,
        string target,
        string glyph,
        params string[] keywords)
    {
        return new ShellCommand($"system:{id}", title, subtitle, _ => LaunchAsync(target))
        {
            Kind = ShellCommandKind.System,
            Glyph = glyph,
            Keywords = keywords,
        };
    }

    private static ValueTask LaunchAsync(string target)
    {
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        return ValueTask.CompletedTask;
    }
}
