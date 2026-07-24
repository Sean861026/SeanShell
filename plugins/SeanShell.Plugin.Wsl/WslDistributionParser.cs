using System.Text.RegularExpressions;

namespace SeanShell.Plugin.Wsl;

public static partial class WslDistributionParser
{
    public static IReadOnlyList<WslDistributionSnapshot> Parse(string output)
    {
        ArgumentNullException.ThrowIfNull(output);

        var distributions = new List<WslDistributionSnapshot>();
        foreach (var rawLine in output
            .Replace("\0", string.Empty, StringComparison.Ordinal)
            .TrimStart('\uFEFF')
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var match = DistributionLine().Match(rawLine);
            if (!match.Success)
            {
                continue;
            }

            var name = match.Groups["name"].Value.Trim();
            if (name.Equals("NAME", StringComparison.OrdinalIgnoreCase) ||
                !int.TryParse(match.Groups["version"].Value, out var version))
            {
                continue;
            }

            distributions.Add(new WslDistributionSnapshot(
                name,
                match.Groups["state"].Value,
                version,
                match.Groups["default"].Success));
        }

        return distributions
            .OrderByDescending(static distribution => distribution.IsDefault)
            .ThenBy(static distribution => distribution.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    [GeneratedRegex(
        @"^\s*(?<default>\*)?\s*(?<name>.+?)\s{2,}(?<state>\S+)\s+(?<version>\d+)\s*$",
        RegexOptions.CultureInvariant)]
    private static partial Regex DistributionLine();
}
