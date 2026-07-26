namespace SeanShell.Gaming;

public sealed record GamingDetectionPerformanceSnapshot(
    TimeSpan? LastScanDuration,
    TimeSpan? P95ScanDuration,
    double? EstimatedCpuPercentage,
    int SampleCount,
    int LastMatchedProcessCount);
