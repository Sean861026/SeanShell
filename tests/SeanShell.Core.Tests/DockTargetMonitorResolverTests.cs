using SeanShell.Core;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class DockTargetMonitorResolverTests
{
    private static readonly IReadOnlyList<DisplayMonitorSnapshot> Monitors =
    [
        CreateMonitor(10, isPrimary: true),
        CreateMonitor(20, isPrimary: false),
    ];

    [TestMethod]
    public void ResolveUsesPreferredMonitorWhenItExists()
    {
        var result = DockTargetMonitorResolver.Resolve(Monitors, 20);

        Assert.AreEqual(1, result);
    }

    [TestMethod]
    public void ResolveFallsBackToPrimaryMonitor()
    {
        var result = DockTargetMonitorResolver.Resolve(Monitors, 99);

        Assert.AreEqual(0, result);
    }

    [TestMethod]
    public void ResolveFallsBackToFirstMonitorWithoutPrimary()
    {
        var monitors = new[]
        {
            CreateMonitor(10, isPrimary: false),
            CreateMonitor(20, isPrimary: false),
        };

        var result = DockTargetMonitorResolver.Resolve(monitors, 99);

        Assert.AreEqual(0, result);
    }

    [TestMethod]
    public void ResolveReturnsNegativeOneWithoutMonitors()
    {
        var result = DockTargetMonitorResolver.Resolve([], 10);

        Assert.AreEqual(-1, result);
    }

    private static DisplayMonitorSnapshot CreateMonitor(
        nint handle,
        bool isPrimary) =>
        new(handle, $"DISPLAY{handle}", 0, 0, 1920, 1080, isPrimary);
}
