using SeanShell.Core;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class PinnedApplicationIdListTests
{
    [TestMethod]
    public void ParseKeepsOrderedDistinctApplicationIds()
    {
        var result = PinnedApplicationIdList.Parse(
            "app:C:\\One.lnk\r\napp:C:\\Two.lnk\nAPP:c:\\one.lnk");

        CollectionAssert.AreEqual(
            new[]
            {
                "app:C:\\One.lnk",
                "app:C:\\Two.lnk",
            },
            result.ToArray());
    }

    [TestMethod]
    public void ParseRejectsNonApplicationAndControlCharacterIds()
    {
        var result = PinnedApplicationIdList.Parse(
            "system:settings\napp:\napp:C:\\Valid.lnk\napp:C:\\Bad\tName.lnk");

        CollectionAssert.AreEqual(
            new[] { "app:C:\\Valid.lnk" },
            result.ToArray());
    }

    [TestMethod]
    public void SerializeCapsPinnedApplications()
    {
        var ids = Enumerable
            .Range(1, PinnedApplicationIdList.MaximumCount + 3)
            .Select(index => $"app:C:\\App{index}.lnk");

        var serialized = PinnedApplicationIdList.Serialize(ids);
        var result = PinnedApplicationIdList.Parse(serialized);

        Assert.HasCount(PinnedApplicationIdList.MaximumCount, result);
        Assert.AreEqual("app:C:\\App1.lnk", result[0]);
        Assert.AreEqual(
            $"app:C:\\App{PinnedApplicationIdList.MaximumCount}.lnk",
            result[^1]);
    }
}
