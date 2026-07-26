using SeanShell.Gaming;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class GamingSessionRecorderTests
{
    private string? _directory;

    [TestCleanup]
    public void Cleanup()
    {
        if (_directory is not null && Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [TestMethod]
    public void ObserveRecordsAndPersistsCompletedDetectedSession()
    {
        var recorder = CreateRecorder(out var store);
        var startedAt = DateTimeOffset.Parse("2026-07-26T10:00:00+08:00");

        var started = recorder.Observe(
            Status(["game"]),
            Performance(),
            startedAt);
        var updated = recorder.Observe(
            Status(["game", "helper"]),
            Performance(),
            startedAt.AddMinutes(2));
        var completed = recorder.Observe(
            Status([]),
            Performance(samples: 40, cpu: 0.025, p95Milliseconds: 8.5),
            startedAt.AddMinutes(10));

        Assert.AreEqual(GamingSessionTransition.Started, started);
        Assert.AreEqual(GamingSessionTransition.Updated, updated);
        Assert.AreEqual(GamingSessionTransition.Completed, completed);
        var record = recorder.Current.RecentSessions.Single();
        CollectionAssert.AreEqual(new[] { "game", "helper" }, record.GameNames.ToArray());
        Assert.AreEqual(TimeSpan.FromMinutes(10), record.Duration);
        Assert.AreEqual(40, record.DetectorSampleCount);
        Assert.AreEqual(0.025, record.EstimatedDetectorCpuPercentage);
        Assert.AreEqual(8.5, record.DetectorP95Milliseconds);

        var reloaded = store.Load();
        Assert.IsNull(reloaded.Warning);
        Assert.AreEqual(record.Id, reloaded.Sessions.Single().Id);
    }

    [TestMethod]
    public void ManualModeWithoutDetectedProcessDoesNotCreateSession()
    {
        var recorder = CreateRecorder(out _);
        var status = new GamingModeStatus(
            ManualModeEnabled: true,
            AutomaticDetectionEnabled: true,
            ConfiguredRuleCount: 1,
            ActiveGameNames: []);

        var transition = recorder.Observe(status, Performance(), DateTimeOffset.UtcNow);

        Assert.AreEqual(GamingSessionTransition.None, transition);
        Assert.IsEmpty(recorder.Current.RecentSessions);
        Assert.IsNull(recorder.Current.ActiveSessionStartedAt);
    }

    [TestMethod]
    public void HistoryKeepsMostRecentTwentySessions()
    {
        var recorder = CreateRecorder(out _);
        var start = DateTimeOffset.Parse("2026-07-01T00:00:00Z");

        for (var index = 0; index < 25; index++)
        {
            recorder.Observe(Status([$"game-{index}"]), Performance(), start.AddHours(index));
            recorder.Observe(Status([]), Performance(), start.AddHours(index).AddMinutes(1));
        }

        Assert.HasCount(20, recorder.Current.RecentSessions);
        CollectionAssert.AreEqual(
            new[] { "game-24" },
            recorder.Current.RecentSessions[0].GameNames.ToArray());
        CollectionAssert.AreEqual(
            new[] { "game-5" },
            recorder.Current.RecentSessions[^1].GameNames.ToArray());
    }

    [TestMethod]
    public void StoreRecoversFromBackupWhenPrimaryIsDamaged()
    {
        var recorder = CreateRecorder(out var store, out var filePath);
        var start = DateTimeOffset.Parse("2026-07-26T00:00:00Z");
        recorder.Observe(Status(["game"]), Performance(), start);
        recorder.Observe(Status([]), Performance(), start.AddMinutes(1));
        File.WriteAllText(filePath, "{ damaged");

        var result = store.Load();

        Assert.IsTrue(result.WasRecovered);
        Assert.IsNotNull(result.Warning);
        Assert.HasCount(1, result.Sessions);
    }

    private GamingSessionRecorder CreateRecorder(out GamingSessionStore store)
    {
        return CreateRecorder(out store, out _);
    }

    private GamingSessionRecorder CreateRecorder(
        out GamingSessionStore store,
        out string filePath)
    {
        _directory = Path.Combine(
            Path.GetTempPath(),
            "SeanShell.Tests",
            Guid.NewGuid().ToString("N"));
        filePath = Path.Combine(_directory, "gaming-sessions.json");
        store = new GamingSessionStore(filePath);
        return new GamingSessionRecorder(
            store,
            store.Load(),
            "Windows test",
            "1.0.0-test");
    }

    private static GamingModeStatus Status(IReadOnlyList<string> names) =>
        new(
            ManualModeEnabled: false,
            AutomaticDetectionEnabled: true,
            ConfiguredRuleCount: 1,
            ActiveGameNames: names);

    private static GamingDetectionPerformanceSnapshot Performance(
        int samples = 1,
        double? cpu = 0.01,
        double? p95Milliseconds = 5) =>
        new(
            LastScanDuration: TimeSpan.FromMilliseconds(4),
            P95ScanDuration: p95Milliseconds is null
                ? null
                : TimeSpan.FromMilliseconds(p95Milliseconds.Value),
            EstimatedCpuPercentage: cpu,
            SampleCount: samples,
            LastMatchedProcessCount: 1);
}
