namespace SeanShell.Gaming;

public sealed record GamingModeStatus(
    bool ManualModeEnabled,
    bool AutomaticDetectionEnabled,
    int ConfiguredRuleCount,
    IReadOnlyList<string> ActiveGameNames)
{
    public bool IsGaming => ManualModeEnabled || ActiveGameNames.Count > 0;
}
