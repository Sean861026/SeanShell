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

    public int RuleCount => _processNames.Count;

    public bool IsGame(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return false;
        }

        return _processNames.Contains(Normalize(processName));
    }

    public static IReadOnlyList<string> ParseRules(string? rules)
    {
        if (string.IsNullOrWhiteSpace(rules))
        {
            return [];
        }

        return rules
            .Split(['\r', '\n', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Normalize)
            .Where(static name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string Normalize(string processName) =>
        Path.GetFileNameWithoutExtension(processName.Trim());
}
