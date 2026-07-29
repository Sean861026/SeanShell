using SeanShell.Core;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class PinnedApplicationOrderTests
{
    [TestMethod]
    public void MergeVisibleOrderPreservesHiddenPinnedSlots()
    {
        string[] applicationIds =
        [
            "app:first",
            "app:hidden",
            "app:second",
            "app:third",
        ];

        var result = PinnedApplicationOrder.MergeVisibleOrder(
            applicationIds,
            ["app:third", "app:first", "app:second"]);

        CollectionAssert.AreEqual(
            new[] { "app:third", "app:hidden", "app:first", "app:second" },
            result.ToArray());
    }

    [TestMethod]
    public void MergeVisibleOrderRejectsIncompleteOrUnknownOrder()
    {
        string[] applicationIds = ["app:first", "app:hidden", "app:second"];

        CollectionAssert.AreEqual(
            applicationIds,
            PinnedApplicationOrder.MergeVisibleOrder(
                applicationIds,
                ["app:second"]).ToArray());
        CollectionAssert.AreEqual(
            applicationIds,
            PinnedApplicationOrder.MergeVisibleOrder(
                applicationIds,
                ["app:first", "app:missing"]).ToArray());
    }

    private static readonly string[] ApplicationIds =
        ["app:first", "app:second", "app:third"];

    [TestMethod]
    public void MoveLeftSwapsWithPreviousApplication()
    {
        var result = PinnedApplicationOrder.Move(
            ApplicationIds,
            "APP:SECOND",
            PinnedApplicationMoveDirection.Left);

        CollectionAssert.AreEqual(
            new[] { "app:second", "app:first", "app:third" },
            result.ToArray());
    }

    [TestMethod]
    public void MoveRightSwapsWithNextApplication()
    {
        var result = PinnedApplicationOrder.Move(
            ApplicationIds,
            "app:second",
            PinnedApplicationMoveDirection.Right);

        CollectionAssert.AreEqual(
            new[] { "app:first", "app:third", "app:second" },
            result.ToArray());
    }

    [DataRow("app:first", PinnedApplicationMoveDirection.Left)]
    [DataRow("app:third", PinnedApplicationMoveDirection.Right)]
    [DataRow("app:missing", PinnedApplicationMoveDirection.Left)]
    [TestMethod]
    public void MoveAtBoundaryOrMissingPreservesOrder(
        string applicationId,
        PinnedApplicationMoveDirection direction)
    {
        var result = PinnedApplicationOrder.Move(
            ApplicationIds,
            applicationId,
            direction);

        CollectionAssert.AreEqual(ApplicationIds, result.ToArray());
    }

    [DataRow("app:first", PinnedApplicationMoveDirection.Left, false)]
    [DataRow("app:first", PinnedApplicationMoveDirection.Right, true)]
    [DataRow("app:second", PinnedApplicationMoveDirection.Left, true)]
    [DataRow("app:second", PinnedApplicationMoveDirection.Right, true)]
    [DataRow("app:third", PinnedApplicationMoveDirection.Right, false)]
    [DataRow("app:missing", PinnedApplicationMoveDirection.Right, false)]
    [TestMethod]
    public void CanMoveReportsOnlyValidAdjacentMoves(
        string applicationId,
        PinnedApplicationMoveDirection direction,
        bool expected)
    {
        Assert.AreEqual(
            expected,
            PinnedApplicationOrder.CanMove(
                ApplicationIds,
                applicationId,
                direction));
    }
}
