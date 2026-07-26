namespace SeanShell.Gaming;

public sealed class GamingDetectionPerformanceMonitor
{
    private const int SampleCapacity = 60;
    private readonly object _gate = new();
    private readonly Queue<Sample> _samples = new();

    public event EventHandler? Changed;

    public GamingDetectionPerformanceSnapshot Current
    {
        get
        {
            lock (_gate)
            {
                return CreateSnapshot();
            }
        }
    }

    public void RecordSample(
        TimeSpan scanDuration,
        TimeSpan processorTime,
        TimeSpan pollingInterval,
        int matchedProcessCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(scanDuration, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(processorTime, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(pollingInterval, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfNegative(matchedProcessCount);

        lock (_gate)
        {
            _samples.Enqueue(new Sample(
                scanDuration,
                processorTime,
                pollingInterval,
                matchedProcessCount));
            if (_samples.Count > SampleCapacity)
            {
                _samples.Dequeue();
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Reset()
    {
        lock (_gate)
        {
            if (_samples.Count == 0)
            {
                return;
            }

            _samples.Clear();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private GamingDetectionPerformanceSnapshot CreateSnapshot()
    {
        if (_samples.Count == 0)
        {
            return new GamingDetectionPerformanceSnapshot(null, null, null, 0, 0);
        }

        var orderedDurations = _samples
            .Select(static sample => sample.ScanDuration)
            .Order()
            .ToArray();
        var percentileIndex = (int)Math.Ceiling(orderedDurations.Length * 0.95) - 1;
        var processorTime = _samples.Sum(static sample => sample.ProcessorTime.TotalMilliseconds);
        var availableProcessorTime = _samples.Sum(static sample =>
            sample.PollingInterval.TotalMilliseconds * Environment.ProcessorCount);
        var estimatedCpuPercentage = availableProcessorTime <= 0
            ? 0
            : Math.Clamp(processorTime / availableProcessorTime * 100, 0, 100);
        var last = _samples.Last();

        return new GamingDetectionPerformanceSnapshot(
            last.ScanDuration,
            orderedDurations[percentileIndex],
            estimatedCpuPercentage,
            _samples.Count,
            last.MatchedProcessCount);
    }

    private sealed record Sample(
        TimeSpan ScanDuration,
        TimeSpan ProcessorTime,
        TimeSpan PollingInterval,
        int MatchedProcessCount);
}
