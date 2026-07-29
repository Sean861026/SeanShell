using SeanShell.Core;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class DockForegroundContinuityTests
{
    [TestMethod]
    public void ApplyMarksPreviousWindowWhenDockOwnsForeground()
    {
        var windows = new[]
        {
            CreateWindow(10),
            CreateWindow(20),
        };

        var result = DockForegroundContinuity.Apply(windows, 20);

        Assert.IsFalse(result[0].IsForeground);
        Assert.IsTrue(result[1].IsForeground);
    }

    [TestMethod]
    public void ApplyPreservesLiveForegroundState()
    {
        var windows = new[]
        {
            CreateWindow(10, isForeground: true),
            CreateWindow(20),
        };

        var result = DockForegroundContinuity.Apply(windows, 20);

        Assert.AreSame(windows, result);
        Assert.IsTrue(result[0].IsForeground);
        Assert.IsFalse(result[1].IsForeground);
    }

    [TestMethod]
    public void ApplyDoesNotGuessWhenPreviousWindowIsMissing()
    {
        var windows = new[]
        {
            CreateWindow(10),
            CreateWindow(20),
        };

        var result = DockForegroundContinuity.Apply(windows, 30);

        Assert.AreSame(windows, result);
        Assert.IsFalse(result.Any(static window => window.IsForeground));
    }

    [TestMethod]
    public void ApplyDoesNotChangeStateWithoutPreviousWindow()
    {
        var windows = new[] { CreateWindow(10) };

        var result = DockForegroundContinuity.Apply(windows, 0);

        Assert.AreSame(windows, result);
    }

    private static DesktopWindowSnapshot CreateWindow(
        nint handle,
        bool isForeground = false) =>
        new(
            handle,
            checked((int)handle),
            $"process-{handle}",
            $"Window {handle}",
            IsMinimized: false,
            MonitorHandle: 1,
            IsForeground: isForeground);
}
