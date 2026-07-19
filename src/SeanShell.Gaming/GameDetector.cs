namespace SeanShell.Gaming;

public sealed class GameDetector
{
    private readonly HashSet<string> _processNames;

    public GameDetector(IEnumerable<string> processNames)
    {
        ArgumentNullException.ThrowIfNull(processNames);
        _processNames = processNames
            .Select(Normalize)
            .Where(static name => name.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public bool IsGame(string processName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(processName);
        return _processNames.Contains(Normalize(processName));
    }

    private static string Normalize(string processName) =>
        Path.GetFileNameWithoutExtension(processName.Trim());
}
