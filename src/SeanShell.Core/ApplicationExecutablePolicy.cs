namespace SeanShell.Core;

public static class ApplicationExecutablePolicy
{
    public static bool IsSupportedLocalPath(string? path) =>
        !string.IsNullOrWhiteSpace(path) &&
        Path.IsPathFullyQualified(path) &&
        !path.StartsWith("\\\\", StringComparison.Ordinal) &&
        Path.GetExtension(path).Equals(
            ".exe",
            StringComparison.OrdinalIgnoreCase);
}
