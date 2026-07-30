namespace SeanShell.Core;

public sealed record AudioEndpointSnapshot(
    bool IsAvailable,
    int? VolumePercent,
    bool IsMuted);

public sealed record AudioEndpointDisplayText(
    string Summary,
    string AccessibleSummary);

public static class AudioEndpointTextFormatter
{
    public static AudioEndpointDisplayText Format(AudioEndpointSnapshot snapshot)
    {
        if (!snapshot.IsAvailable || snapshot.VolumePercent is null)
        {
            return new AudioEndpointDisplayText(
                "Sound — Status unavailable",
                "Sound status unavailable.");
        }

        var percent = Math.Clamp(snapshot.VolumePercent.Value, 0, 100);
        var state = snapshot.IsMuted ? "Muted" : $"{percent}%";
        var accessibleState = snapshot.IsMuted
            ? $"muted at {percent} percent"
            : $"{percent} percent";
        return new AudioEndpointDisplayText(
            $"Sound — {state}",
            $"Sound {accessibleState}.");
    }
}
