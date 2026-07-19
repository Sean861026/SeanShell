namespace SeanShell.Core;

public sealed record DisplayMonitorSnapshot(
    nint Handle,
    string DeviceName,
    int WorkAreaX,
    int WorkAreaY,
    int WorkAreaWidth,
    int WorkAreaHeight,
    bool IsPrimary);
