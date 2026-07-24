namespace SeanShell.Plugin.Wsl;

public sealed record WslDistributionSnapshot(
    string Name,
    string State,
    int Version,
    bool IsDefault)
{
    public string StatusText
    {
        get
        {
            var prefix = IsDefault ? "Default \u00B7 " : string.Empty;
            return $"{prefix}{State} \u00B7 WSL {Version}";
        }
    }
}
