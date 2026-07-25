using SeanShell.Core;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class StartupCrashLoopGuardTests
{
    [TestMethod]
    public void CleanExitDoesNotCountAsFailure()
    {
        using var directory = new TemporaryDirectory();
        var guard = CreateGuard(directory);

        var first = guard.BeginSession(automaticStartup: true);
        Assert.IsTrue(first.CanStart);
        Assert.IsTrue(guard.MarkCleanExit(first.SessionId!.Value));

        var second = guard.BeginSession(automaticStartup: true);

        Assert.IsTrue(second.CanStart);
        Assert.AreEqual(0, second.ConsecutiveFailures);
    }

    [TestMethod]
    public void ThirdConsecutiveIncompleteStartupDisablesAutomaticStartup()
    {
        using var directory = new TemporaryDirectory();
        var guard = CreateGuard(directory);

        Assert.IsTrue(guard.BeginSession(automaticStartup: true).CanStart);
        Assert.IsTrue(guard.BeginSession(automaticStartup: true).CanStart);
        Assert.IsTrue(guard.BeginSession(automaticStartup: true).CanStart);

        var blocked = guard.BeginSession(automaticStartup: true);

        Assert.IsFalse(blocked.CanStart);
        Assert.IsTrue(blocked.AutomaticStartupDisabled);
        Assert.AreEqual(StartupCrashLoopGuard.FailureThreshold, blocked.ConsecutiveFailures);
    }

    [TestMethod]
    public void ManualLaunchCanRecoverDisabledAutomaticStartup()
    {
        using var directory = new TemporaryDirectory();
        var guard = CreateGuard(directory);

        guard.BeginSession(automaticStartup: true);
        guard.BeginSession(automaticStartup: true);
        guard.BeginSession(automaticStartup: true);
        Assert.IsFalse(guard.BeginSession(automaticStartup: true).CanStart);

        var manual = guard.BeginSession(automaticStartup: false);
        Assert.IsTrue(manual.CanStart);
        Assert.IsTrue(guard.MarkHealthy(manual.SessionId!.Value));

        var automatic = guard.BeginSession(automaticStartup: true);
        Assert.IsTrue(automatic.CanStart);
        Assert.AreEqual(0, automatic.ConsecutiveFailures);
    }

    [TestMethod]
    public void StaleSessionCannotClearCurrentSession()
    {
        using var directory = new TemporaryDirectory();
        var guard = CreateGuard(directory);
        var first = guard.BeginSession(automaticStartup: false);
        var second = guard.BeginSession(automaticStartup: false);

        Assert.IsFalse(guard.MarkHealthy(first.SessionId!.Value));
        Assert.IsTrue(guard.MarkHealthy(second.SessionId!.Value));
    }

    [TestMethod]
    public void DamagedStateAllowsManualRecovery()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "startup-health.json");
        File.WriteAllText(path, "not-json");
        var guard = new StartupCrashLoopGuard(path);

        var result = guard.BeginSession(automaticStartup: false);

        Assert.IsTrue(result.CanStart);
        Assert.IsNotNull(result.Warning);
        Assert.IsTrue(guard.MarkHealthy(result.SessionId!.Value));
    }

    private static StartupCrashLoopGuard CreateGuard(TemporaryDirectory directory) =>
        new(Path.Combine(directory.Path, "startup-health.json"));

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"SeanShell.Tests.{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
