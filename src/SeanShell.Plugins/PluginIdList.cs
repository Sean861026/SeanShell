namespace SeanShell.Plugins;

public static class PluginIdList
{
    private static readonly char[] Separators = ['\r', '\n', ',', ';'];

    public static IReadOnlyList<string> Parse(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value
                .Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();

    public static string Serialize(IEnumerable<string> pluginIds)
    {
        ArgumentNullException.ThrowIfNull(pluginIds);
        return string.Join(Environment.NewLine, pluginIds
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Select(static id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase));
    }
}
