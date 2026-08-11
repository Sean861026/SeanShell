using SeanShell.Core;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class FullShellReadinessResolverTests
{
    [TestMethod]
    [DataRow("Enterprise")]
    [DataRow("EnterpriseS")]
    [DataRow("Education")]
    [DataRow("IoTEnterprise")]
    public void Resolve_SupportedEdition_ReportsSafetyWorkPending(string editionId)
    {
        var snapshot = FullShellReadinessResolver.Resolve("Windows 11", editionId);

        Assert.AreEqual(FullShellReadinessState.SafetyWorkPending, snapshot.State);
        Assert.IsTrue(snapshot.IsSupportedEdition);
    }

    [TestMethod]
    [DataRow("Core")]
    [DataRow("Professional")]
    public void Resolve_UnsupportedEdition_KeepsCompanionTaskbarAvailable(string editionId)
    {
        var snapshot = FullShellReadinessResolver.Resolve("Windows 11", editionId);

        Assert.AreEqual(FullShellReadinessState.UnsupportedEdition, snapshot.State);
        StringAssert.Contains(snapshot.Message, "Companion Taskbar");
    }

    [TestMethod]
    public void Resolve_MissingEdition_FailsClosed()
    {
        var snapshot = FullShellReadinessResolver.Resolve(null, " ");

        Assert.AreEqual(FullShellReadinessState.Unavailable, snapshot.State);
        Assert.IsFalse(snapshot.IsSupportedEdition);
        Assert.AreEqual("Unknown", snapshot.EditionId);
    }
}
