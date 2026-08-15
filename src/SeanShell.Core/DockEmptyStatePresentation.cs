namespace SeanShell.Core;

public static class DockEmptyStatePresentation
{
    private const string LoadingGlyph = "\uE895";
    private const string EmptyGlyph = "\uE8A7";
    private const string WarningGlyph = "\uE783";

    public static DockEmptyStateState Loading() =>
        new(LoadingGlyph, "Loading open windows", false);

    public static DockEmptyStateState NoWindows(string? deviceName)
    {
        var monitorName = Normalize(deviceName, "this display");
        return new(
            EmptyGlyph,
            $"No open application windows on {monitorName}",
            false);
    }

    public static DockEmptyStateState Unavailable(string? message)
    {
        var reason = Normalize(message, "Window information is temporarily unavailable");
        return new(
            WarningGlyph,
            $"Dock unavailable. {reason}",
            true);
    }

    private static string Normalize(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return string.Join(
            ' ',
            value.Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }
}

public sealed record DockEmptyStateState(
    string Glyph,
    string Description,
    bool IsError);
