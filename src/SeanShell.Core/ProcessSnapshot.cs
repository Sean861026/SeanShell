namespace SeanShell.Core;

public sealed record ProcessSnapshot(int Id, string Name, DateTimeOffset ObservedAt);
