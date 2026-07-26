namespace SeanShell.Gaming;

public sealed record GamingSessionHistorySnapshot(
    DateTimeOffset? ActiveSessionStartedAt,
    IReadOnlyList<GamingSessionRecord> RecentSessions,
    string? Warning);
