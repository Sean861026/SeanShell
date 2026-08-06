using System.Diagnostics;
using SeanShell.Core;

namespace SeanShell.Windows;

public sealed class InstalledApplicationProvider : ILauncherCommandProvider
{
    private readonly object _gate = new();
    private readonly object _iconGate = new();
    private readonly Dictionary<string, ApplicationIconSnapshot?> _iconCache =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly NativeApplicationIconReader _iconReader = new();
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

    public async Task<IReadOnlyList<ShellCommand>> GetByIdsAsync(
        IEnumerable<string> applicationIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(applicationIds);
        var requestedIds = PinnedApplicationIdList.Parse(
            PinnedApplicationIdList.Serialize(applicationIds));
        if (requestedIds.Count == 0)
        {
            return [];
        }

        var index = await GetOrCreateIndexTask()
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        var byId = index.ToDictionary(
            static command => command.Id,
            StringComparer.OrdinalIgnoreCase);
        var selected = requestedIds
            .Where(byId.ContainsKey)
            .ToArray();
        return await Task.Run(
                () => selected
                    .Select(id => AddIcon(byId[id]))
                    .ToArray(),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private ShellCommand AddIcon(ShellCommand command)
    {
        if (command.Icon is not null ||
            string.IsNullOrWhiteSpace(command.IconSourcePath))
        {
            return command;
        }

        ApplicationIconSnapshot? icon;
        lock (_iconGate)
        {
            if (!_iconCache.TryGetValue(command.IconSourcePath, out icon))
            {
                if (_iconCache.Count >= PinnedApplicationIdList.MaximumCount * 4)
                {
                    _iconCache.Clear();
                }

                icon = _iconReader.ReadFileIcon(command.IconSourcePath);
                _iconCache[command.IconSourcePath] = icon;
            }
        }

        return icon is null ? command : command with { Icon = icon };
    }

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
        var shortcutTarget = ShellShortcutTargetResolver.Resolve(path);
        return new ShellCommand(
            $"app:{path}",
            title,
            string.IsNullOrWhiteSpace(parent) ? "Installed application" : parent,
            _ => LaunchAsync(path))
        {
            Kind = ShellCommandKind.Application,
            Keywords = [title, parent ?? string.Empty, "app", "application", "program"],
            Glyph = "\uE8B7",
            IconSourcePath = path,
            ApplicationProcessName = shortcutTarget?.ProcessName,
            ApplicationExecutablePath = shortcutTarget?.ExecutablePath,
            ApplicationArguments = shortcutTarget?.Arguments,
        };
    }

    private static ValueTask LaunchAsync(string path)
    {
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        return ValueTask.CompletedTask;
    }
}
