namespace SeanShell.Core;

public enum ActivityIslandContentKind
{
    Activation,
    InputLayout,
    Preview,
}

/// <summary>Presentation-only data; never performs or confirms a system action.</summary>
public sealed record ActivityIslandContent
{
    private ActivityIslandContent(ActivityIslandContentKind kind, string title, string detail)
    {
        Kind = kind;
        Title = title;
        Detail = detail;
    }

    public ActivityIslandContentKind Kind { get; }
    public string Title { get; }
    public string Detail { get; }

    public string Label => Kind switch
    {
        ActivityIslandContentKind.InputLayout => "KEYBOARD LAYOUT",
        ActivityIslandContentKind.Preview => "PREVIEW",
        _ => "NOW ACTIVE",
    };

    public string Glyph => Kind == ActivityIslandContentKind.InputLayout ? "\uE765" : "\uE8A7";

    public string AccessibleName => Kind switch
    {
        ActivityIslandContentKind.InputLayout => $"Keyboard layout: {Title}. {Detail}",
        ActivityIslandContentKind.Preview => $"Activity Island preview: {Title}. {Detail}",
        _ => $"Now active: {Title}. {Detail}",
    };

    public static ActivityIslandContent ForActivation(string processName, string windowTitle)
    {
        ArgumentNullException.ThrowIfNull(processName);
        ArgumentNullException.ThrowIfNull(windowTitle);
        return new(ActivityIslandContentKind.Activation,
            string.IsNullOrWhiteSpace(processName) ? "Application" : processName,
            windowTitle);
    }

    public static ActivityIslandContent ForInputLayout(string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        return new(ActivityIslandContentKind.InputLayout, displayName,
            "Confirmed for the selected application. IME mode is separate.");
    }

    public static ActivityIslandContent ForEnabledPreview() => new(
        ActivityIslandContentKind.Preview,
        "Activity Island enabled",
        "Preview for this session · tap to dismiss");
}
