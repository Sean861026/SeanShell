using System.Diagnostics;
using SeanShell.Core;

namespace SeanShell.Windows;

public sealed class InstalledApplicationProvider : ILauncherCommandProvider
{
    private readonly object _gate = new();
    private Task<IReadOnlyList<ShellCommand>>? _indexTask;

    public ValueTask<IReadOnlyList<ShellCommand>> GetCommandsAsync(
        string query,
        CancellationToken cancellationToken)
    {
        return new ValueTask<IReadOnlyList<ShellCommand>>(
            GetOrCreateIndexTask().WaitAsync(cancellationToken));
    }

    public Task<IReadOnlyList<ShellCommand>> WarmAsync(CancellationToken cancellationToken = default) =>
        GetOrCreateIndexTask().WaitAsync(cancellationToken);

    private Task<IReadOnlyList<ShellCommand>> GetOrCreateIndexTask()
    {
        lock (_gate)
        {
            return _indexTask ??= Task.Run(BuildIndex);
        }
    }

    private static IReadOnlyList<ShellCommand> BuildIndex()
    {
        var options = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = true,
            MaxRecursionDepth = 8,
        };

        return GetStartMenuRoots()
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFiles(root, "*", options))
            .Where(IsLaunchableShortcut)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(CreateCommand)
            .Where(static command => command is not null)
            .Cast<ShellCommand>()
            .OrderBy(static command => command.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<string> GetStartMenuRoots()
    {
        yield return Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu);
    }

    private static bool IsLaunchableShortcut(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".url", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".appref-ms", StringComparison.OrdinalIgnoreCase);
    }

    private static ShellCommand? CreateCommand(string path)
    {
        var title = Path.GetFileNameWithoutExtension(path).Trim();
        if (title.Length == 0)
        {
            return null;
        }

        var parent = Path.GetFileName(Path.GetDirectoryName(path));
        return new ShellCommand(
            $"app:{path}",
            title,
            string.IsNullOrWhiteSpace(parent) ? "Installed application" : parent,
            _ => LaunchAsync(path))
        {
            Kind = ShellCommandKind.Application,
            Keywords = [title, parent ?? string.Empty, "app", "application", "program"],
            Glyph = "\uE8B7",
        };
    }

    private static ValueTask LaunchAsync(string path)
    {
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        return ValueTask.CompletedTask;
    }
}
