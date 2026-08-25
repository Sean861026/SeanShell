using SeanShell.Plugin.DeveloperTools;
using SeanShell.PluginBroker.Runtime;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class PluginEntryPointInspectorTests
{
    [TestMethod]
    public void ValidPublicPluginEntryPointIsAcceptedWithoutLoadingIt()
    {
        var error = PluginEntryPointInspector.Validate(
            typeof(DeveloperToolsPlugin).Assembly.Location,
            typeof(DeveloperToolsPlugin).FullName!);

        Assert.IsNull(error);
    }

    [TestMethod]
    public void MissingEntryTypeIsRejected()
    {
        var error = PluginEntryPointInspector.Validate(
            typeof(DeveloperToolsPlugin).Assembly.Location,
            "SeanShell.Plugin.DeveloperTools.MissingPlugin");

        StringAssert.Contains(error, "not found exactly once");
    }

    [TestMethod]
    public void TypeWithoutPluginContractIsRejected()
    {
        var error = PluginEntryPointInspector.Validate(
            typeof(PluginEntryPointInspector).Assembly.Location,
            typeof(PluginDependencyLoadContext).FullName!);

        StringAssert.Contains(error, "directly implement ISeanShellPlugin");
    }

    [TestMethod]
    public void UnmanagedOrMalformedAssemblyIsRejectedWithoutExecution()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "SeanShell.EntryPointInspector.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "Malformed.dll");
        File.WriteAllBytes(path, [0x4d, 0x5a, 0x00, 0x00]);
        try
        {
            var error = PluginEntryPointInspector.Validate(path, "Example.Publisher.Plugin");

            StringAssert.Contains(error, "metadata");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
