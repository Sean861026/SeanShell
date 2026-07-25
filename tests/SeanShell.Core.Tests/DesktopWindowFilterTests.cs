using SeanShell.Core;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class DesktopWindowFilterTests
{
    [TestMethod]
    public void ReturnsOnlyWindowsForRequestedMonitor()
    {
        var windows = new[]
        {
            CreateWindow(1, 10),
            CreateWindow(2, 20),
            CreateWindow(3, 10),
        };

        var selected = DesktopWindowFilter.ForMonitor(windows, 10);

        CollectionAssert.AreEqual(
            new nint[] { 1, 3 },
            selected.Select(static window => window.Handle).ToArray());
    }

    [TestMethod]
    public void CapsWindowCountWithoutChangingOrder()
    {
        var windows = Enumerable.Range(1, 20)
            .Select(index => CreateWindow(index, 10))
            .ToArray();

        var selected = DesktopWindowFilter.ForMonitor(windows, 10, maximumCount: 4);

        CollectionAssert.AreEqual(
            new nint[] { 1, 2, 3, 4 },
            selected.Select(static window => window.Handle).ToArray());
    }

    private static DesktopWindowSnapshot CreateWindow(nint handle, nint monitorHandle) =>
        new(
            handle,
            checked((int)handle),
            $"Process {handle}",
            $"Window {handle}",
            false,
            monitorHandle);
}
