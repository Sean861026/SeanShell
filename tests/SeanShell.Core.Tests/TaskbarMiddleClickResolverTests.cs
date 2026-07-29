using SeanShell.Core;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class TaskbarMiddleClickResolverTests
{
    [TestMethod]
    [DataRow(0, TaskbarMiddleClickAction.None)]
    [DataRow(1, TaskbarMiddleClickAction.Open)]
    [DataRow(2, TaskbarMiddleClickAction.Choose)]
    [DataRow(8, TaskbarMiddleClickAction.Choose)]
    public void ResolveRequiresAnUnambiguousApplication(
        int candidateCount,
        TaskbarMiddleClickAction expected)
    {
        Assert.AreEqual(
            expected,
            TaskbarMiddleClickResolver.Resolve(candidateCount));
    }

    [TestMethod]
    public void ResolveRejectsNegativeCandidateCounts()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => TaskbarMiddleClickResolver.Resolve(-1));
    }
}
