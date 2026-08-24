using SeanShell.PluginBroker.Protocol;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class PluginBrokerActivationContractTests
{
    [TestMethod]
    public void ValidEntryTypeAndCapabilitySubsetAreAccepted()
    {
        Assert.IsNull(PluginBrokerActivationContract.Validate(
            new PluginBrokerActivationRequest(
                "Example.Publisher.LauncherPlugin",
                RequestedCapabilities: 1),
            grantedCapabilities: 3));
    }

    [DataRow("")]
    [DataRow("LauncherPlugin")]
    [DataRow("Example..LauncherPlugin")]
    [DataRow("Example.2LauncherPlugin")]
    [DataRow("Example.Launcher-Plugin")]
    [DataRow("Example.LauncherPlugin`1")]
    [TestMethod]
    public void InvalidEntryTypesAreRejected(string entryType)
    {
        Assert.IsNotNull(PluginBrokerActivationContract.Validate(
            new PluginBrokerActivationRequest(entryType, 1),
            grantedCapabilities: 1));
    }

    [DataRow(0, 1)]
    [DataRow(4, 7)]
    [DataRow(2, 1)]
    [DataRow(1, 0)]
    [DataRow(1, 4)]
    [TestMethod]
    public void MissingUnknownOrEscalatedCapabilitiesAreRejected(
        int requestedCapabilities,
        int grantedCapabilities)
    {
        Assert.IsNotNull(PluginBrokerActivationContract.Validate(
            new PluginBrokerActivationRequest(
                "Example.Publisher.LauncherPlugin",
                requestedCapabilities),
            grantedCapabilities));
    }

    [TestMethod]
    public void OversizedEntryTypeIsRejected()
    {
        var entryType = "Example." +
            new string('A', PluginBrokerActivationContract.MaximumEntryTypeCharacters);

        Assert.IsNotNull(PluginBrokerActivationContract.Validate(
            new PluginBrokerActivationRequest(entryType, 1),
            grantedCapabilities: 1));
    }
}
