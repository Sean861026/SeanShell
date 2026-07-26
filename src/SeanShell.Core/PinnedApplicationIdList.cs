namespace SeanShell.Core;

public static class PinnedApplicationIdList
{
    public const int MaximumCount = 8;
    private const int MaximumIdLength = 2048;
    private const string ApplicationPrefix = "app:";

    public static IReadOnlyList<string> Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        return value
            .Split(
                ['\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries)
            .Where(IsValid)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaximumCount)
            .ToArray();
    }

    public static string Serialize(IEnumerable<string> applicationIds)
    {
        ArgumentNullException.ThrowIfNull(applicationIds);
        return string.Join(Environment.NewLine, applicationIds
            .Where(IsValid)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaximumCount));
    }

    private static bool IsValid(string applicationId) =>
        applicationId.Length > ApplicationPrefix.Length &&
        applicationId.Length <= MaximumIdLength &&
        applicationId.StartsWith(
            ApplicationPrefix,
            StringComparison.OrdinalIgnoreCase) &&
        !applicationId.Any(char.IsControl);
}
