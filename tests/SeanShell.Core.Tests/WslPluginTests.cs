using SeanShell.Plugin.Wsl;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class WslPluginTests
{
    [TestMethod]
    public void ShellStartInfoUsesSystemExecutableAndUserProfile()
    {
        var startInfo = WslShellStartInfoFactory.Create("Ubuntu Test");

        Assert.IsTrue(Path.IsPathRooted(startInfo.FileName));
        Assert.AreEqual(
            "wsl.exe",
            Path.GetFileName(startInfo.FileName),
            true);
        Assert.AreEqual(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            startInfo.WorkingDirectory);
        Assert.IsTrue(startInfo.UseShellExecute);
        CollectionAssert.AreEqual(
            new[] { "--distribution", "Ubuntu Test" },
            startInfo.ArgumentList.ToArray());
    }

    [TestMethod]
    public void ParserReadsDefaultStateAndVersion()
    {
        const string output =
            "  NAME                   STATE           VERSION\r\n" +
            "* Ubuntu                 Stopped         2\r\n" +
            "  docker-desktop         Running         2\r\n";

        var distributions = WslDistributionParser.Parse(output);

        Assert.HasCount(2, distributions);
        Assert.AreEqual("Ubuntu", distributions[0].Name);
        Assert.IsTrue(distributions[0].IsDefault);
        Assert.AreEqual("Stopped", distributions[0].State);
        Assert.AreEqual(2, distributions[0].Version);
        Assert.AreEqual("Default \u00B7 Stopped \u00B7 WSL 2", distributions[0].StatusText);
        Assert.AreEqual("docker-desktop", distributions[1].Name);
        Assert.IsFalse(distributions[1].IsDefault);
    }

    [TestMethod]
    public void ParserHandlesRedirectedUtf16NullCharacters()
    {
        const string output =
            "  NAME        STATE      VERSION\r\n" +
            "* Ubuntu      Stopped    2\r\n";
        var redirectedOutput = string.Concat(
            output.SelectMany(static character => new[] { character, '\0' }));

        var distributions = WslDistributionParser.Parse(redirectedOutput);

        Assert.HasCount(1, distributions);
        Assert.AreEqual("Ubuntu", distributions[0].Name);
    }

    [TestMethod]
    public async Task PluginReturnsCachedCommandsAndRefreshesState()
    {
        var provider = new StubProvider();
        await using var plugin = new WslPlugin(provider);

        await plugin.InitializeAsync(CancellationToken.None);
        var initialCommands = await plugin.GetCommandsAsync(
            string.Empty,
            CancellationToken.None);

        Assert.HasCount(3, initialCommands);
        Assert.AreEqual(
            "Default \u00B7 Stopped \u00B7 WSL 2 \u00B7 Open Linux shell",
            initialCommands.Single(command => command.Title == "Ubuntu WSL shell").Subtitle);
        Assert.IsTrue(initialCommands.Any(command => command.Title == "Ubuntu WSL files"));
        Assert.IsFalse(initialCommands.Any(command => command.Title == "docker-desktop"));

        var refresh = initialCommands.Single(
            command => command.Title == "Refresh WSL distributions");
        await refresh.ExecuteAsync(CancellationToken.None);
        var refreshedCommands = await plugin.GetCommandsAsync(
            string.Empty,
            CancellationToken.None);

        Assert.AreEqual(
            "Default \u00B7 Running \u00B7 WSL 2 \u00B7 Open Linux shell",
            refreshedCommands.Single(command => command.Title == "Ubuntu WSL shell").Subtitle);
        Assert.AreEqual(2, provider.CallCount);
    }

    private sealed class StubProvider : IWslDistributionProvider
    {
        public int CallCount { get; private set; }

        public ValueTask<IReadOnlyList<WslDistributionSnapshot>> GetDistributionsAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return ValueTask.FromResult<IReadOnlyList<WslDistributionSnapshot>>(
            [
                new("Ubuntu", CallCount == 1 ? "Stopped" : "Running", 2, true),
                new("docker-desktop", "Stopped", 2, false),
            ]);
        }
    }
}
