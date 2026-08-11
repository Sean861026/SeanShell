using SeanShell.Core;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class LauncherTargetMonitorResolverTests
{
    private static readonly IReadOnlyList<DisplayMonitorSnapshot> Monitors =
    [
        CreateMonitor(10, isPrimary: true),
        CreateMonitor(20, isPrimary: false),
        CreateMonitor(30, isPrimary: false),
    ];

    [TestMethod]
    public void ResolvePrefersExplicitRequestOverForegroundMonitor()
    {
        var result = LauncherTargetMonitorResolver.Resolve(Monitors, 30, 20);

        Assert.AreEqual(2, result);
    }

    [TestMethod]
    public void ResolveUsesForegroundWhenExplicitRequestIsUnavailable()
    {
        var result = LauncherTargetMonitorResolver.Resolve(Monitors, 99, 20);

        Assert.AreEqual(1, result);
    }

    [TestMethod]
    public void ResolveFallsBackToPrimaryThenFirstMonitor()
    {
        var primaryResult = LauncherTargetMonitorResolver.Resolve(Monitors, 99, 98);
        var firstResult = LauncherTargetMonitorResolver.Resolve(
            [CreateMonitor(40, isPrimary: false)],
            99,
            98);

        Assert.AreEqual(0, primaryResult);
        Assert.AreEqual(0, firstResult);
    }

    [TestMethod]
    public void ResolveReturnsNegativeOneWhenNoMonitorExists()
    {
        var result = LauncherTargetMonitorResolver.Resolve([], 10, 20);

        Assert.AreEqual(-1, result);
    }

    private static DisplayMonitorSnapshot CreateMonitor(nint handle, bool isPrimary) =>
        new(handle, $"Display {handle}", 0, 0, 1920, 1080, isPrimary);
}
