namespace SeanShell.Gaming;

public sealed record GamingSessionLoadResult(
    IReadOnlyList<GamingSessionRecord> Sessions,
    bool WasRecovered = false,
    string? Warning = null);
