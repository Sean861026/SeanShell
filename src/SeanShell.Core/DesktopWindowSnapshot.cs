namespace SeanShell.Core;

public sealed record DesktopWindowSnapshot(
    nint Handle,
    int ProcessId,
    string ProcessName,
    string Title,
    bool IsMinimized,
    nint MonitorHandle);
