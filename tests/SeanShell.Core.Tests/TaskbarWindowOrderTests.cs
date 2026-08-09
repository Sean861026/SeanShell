using SeanShell.Core;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class TaskbarWindowOrderTests
{
    [TestMethod]
    public void FirstSnapshotKeepsIncomingOrder()
    {
        var groups = new[]
        {
            CreateGroup("Code", 1),
            CreateGroup("Terminal", 2),
        };

        var result = TaskbarWindowOrder.Apply(groups, []);

        CollectionAssert.AreEqual(
            new[] { "Code", "Terminal" },
            result.Groups.Select(static group => group.ProcessName).ToArray());
    }

    [TestMethod]
    public void NewGroupAppendsWithoutMovingExistingGroups()
    {
        var existing = new[]
        {
            CreateGroup("Alpha", 1),
            CreateGroup("Zulu", 2),
        };
        var initial = TaskbarWindowOrder.Apply(existing, []);
        var alphabeticRefresh = new[]
        {
            CreateGroup("Alpha", 1),
            CreateGroup("Middle", 3),
            CreateGroup("Zulu", 2),
        };

        var result = TaskbarWindowOrder.Apply(alphabeticRefresh, initial.Keys);

        CollectionAssert.AreEqual(
            new[] { "Alpha", "Zulu", "Middle" },
            result.Groups.Select(static group => group.ProcessName).ToArray());
    }

    [TestMethod]
    public void ManualOrderIsPreservedByTheNextSnapshot()
    {
        var groups = new[]
        {
            CreateGroup("Alpha", 1),
            CreateGroup("Terminal", 2),
            CreateGroup("Zulu", 3),
        };
        var keys = groups
            .Select(TaskbarWindowGrouper.GetKey)
            .Reverse()
            .ToArray();

        var result = TaskbarWindowOrder.Apply(groups, keys);

        CollectionAssert.AreEqual(
            new[] { "Zulu", "Terminal", "Alpha" },
            result.Groups.Select(static group => group.ProcessName).ToArray());
    }

    [TestMethod]
    public void ClosedGroupsAreRemovedFromRememberedOrder()
    {
        var previous = TaskbarWindowOrder.Apply(
            [CreateGroup("Alpha", 1), CreateGroup("Terminal", 2)],
            []);

        var result = TaskbarWindowOrder.Apply(
            [CreateGroup("Terminal", 2)],
            previous.Keys);

        CollectionAssert.AreEqual(
            new[] { "Terminal" },
            result.Groups.Select(static group => group.ProcessName).ToArray());
        Assert.HasCount(1, result.Keys);
    }

    [TestMethod]
    public void GenericHostsUseTheirWindowHandleAsIdentity()
    {
        var first = CreateGroup("ApplicationFrameHost", 10);
        var second = CreateGroup("ApplicationFrameHost", 20);

        Assert.AreNotEqual(
            TaskbarWindowGrouper.GetKey(first),
            TaskbarWindowGrouper.GetKey(second));
    }

    [TestMethod]
    public void MoveSwapsTheRequestedRunningGroup()
    {
        string[] keys = ["process:Alpha", "process:Terminal", "process:Zulu"];

        var result = TaskbarWindowOrder.Move(
            keys,
            "PROCESS:TERMINAL",
            TaskbarWindowMoveDirection.Left);

        CollectionAssert.AreEqual(
            new[] { "process:Terminal", "process:Alpha", "process:Zulu" },
            result.ToArray());
    }

    [DataRow("process:Alpha", TaskbarWindowMoveDirection.Left, false)]
    [DataRow("process:Alpha", TaskbarWindowMoveDirection.Right, true)]
    [DataRow("process:Terminal", TaskbarWindowMoveDirection.Left, true)]
    [DataRow("process:Zulu", TaskbarWindowMoveDirection.Right, false)]
    [TestMethod]
    public void CanMoveReportsValidRunningGroupMoves(
        string key,
        TaskbarWindowMoveDirection direction,
        bool expected)
    {
        string[] keys = ["process:Alpha", "process:Terminal", "process:Zulu"];

        Assert.AreEqual(expected, TaskbarWindowOrder.CanMove(keys, key, direction));
    }

    private static TaskbarWindowGroup CreateGroup(
        string processName,
        nint handle) =>
        new(
            processName,
            [
                new DesktopWindowSnapshot(
                    handle,
                    (int)handle,
                    processName,
                    $"{processName} window",
                    false,
                    1),
            ]);
}
