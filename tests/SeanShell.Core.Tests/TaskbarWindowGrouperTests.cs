using SeanShell.Core;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class TaskbarWindowGrouperTests
{
    [TestMethod]
    public void GroupCombinesProcessWindowsAndKeepsForegroundFirst()
    {
        DesktopWindowSnapshot[] windows =
        [
            CreateWindow(1, "Code", "README", minimized: true),
            CreateWindow(2, "vivaldi", "Docs"),
            CreateWindow(3, "code", "Program.cs", foreground: true),
        ];

        var groups = TaskbarWindowGrouper.Group(windows);

        Assert.HasCount(2, groups);
        Assert.AreEqual("Code", groups[0].ProcessName);
        Assert.HasCount(2, groups[0].Windows);
        Assert.AreEqual((nint)3, groups[0].PrimaryWindow.Handle);
        Assert.IsTrue(groups[0].IsForeground);
        Assert.IsFalse(groups[0].IsMinimized);
        Assert.AreEqual("vivaldi", groups[1].ProcessName);
    }

    [TestMethod]
    public void GroupMarksGroupMinimizedOnlyWhenEveryWindowIsMinimized()
    {
        var groups = TaskbarWindowGrouper.Group(
        [
            CreateWindow(1, "Code", "One", minimized: true),
            CreateWindow(2, "Code", "Two", minimized: true),
        ]);
        Assert.HasCount(1, groups);
        var group = groups[0];

        Assert.IsTrue(group.IsMinimized);
        Assert.IsFalse(group.IsForeground);
    }

    [DataRow("Application")]
    [DataRow("ApplicationFrameHost")]
    [DataRow("RuntimeBroker")]
    [TestMethod]
    public void GroupDoesNotCombineGenericShellHosts(string processName)
    {
        var groups = TaskbarWindowGrouper.Group(
        [
            CreateWindow(1, processName, "One"),
            CreateWindow(2, processName, "Two"),
        ]);

        Assert.HasCount(2, groups);
        Assert.IsTrue(groups.All(static group => group.Windows.Count == 1));
    }

    private static DesktopWindowSnapshot CreateWindow(
        nint handle,
        string processName,
        string title,
        bool minimized = false,
        bool foreground = false) =>
        new(
            handle,
            checked((int)handle),
            processName,
            title,
            minimized,
            10,
            foreground);
}
