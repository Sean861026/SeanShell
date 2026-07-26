using System.Text.Json.Serialization;

namespace SeanShell.Gaming;

public sealed record GamingSessionRecord(
    Guid Id,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    IReadOnlyList<string> GameNames,
    int DetectorSampleCount,
    double? EstimatedDetectorCpuPercentage,
    double? DetectorP95Milliseconds,
    string WindowsVersion,
    string SeanShellVersion)
{
    [JsonIgnore]
    public TimeSpan Duration => EndedAt - StartedAt;
}
