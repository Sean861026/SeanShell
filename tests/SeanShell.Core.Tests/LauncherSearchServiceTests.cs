using SeanShell.Core;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class LauncherSearchServiceTests
{
    [TestMethod]
    public async Task SearchAsync_RanksDeduplicatesAndLimitsResults()
    {
        var exact = CreateCommand("settings", "Settings");
        var duplicate = exact with { Subtitle = "Duplicate" };
        var secondary = CreateCommand("terminal", "Terminal");
        var service = new LauncherSearchService(
        [
            new StubProvider([secondary, duplicate]),
            new StubProvider([exact]),
        ]);

        var results = await service.SearchAsync("Settings", 1, CancellationToken.None);

        Assert.HasCount(1, results);
        Assert.AreEqual("settings", results[0].Id);
    }

    [TestMethod]
    public async Task SearchAsync_IsolatesProviderFailures()
    {
        var command = CreateCommand("terminal", "Terminal");
        var service = new LauncherSearchService(
        [
            new ThrowingProvider(),
            new StubProvider([command]),
        ]);

        var results = await service.SearchAsync("term", 8, CancellationToken.None);

        Assert.HasCount(1, results);
        Assert.AreEqual(command, results[0]);
    }

    private static ShellCommand CreateCommand(string id, string title) =>
        new(id, title, null, _ => ValueTask.CompletedTask);

    private sealed class StubProvider(IReadOnlyList<ShellCommand> commands) : ILauncherCommandProvider
    {
        public ValueTask<IReadOnlyList<ShellCommand>> GetCommandsAsync(
            string query,
            CancellationToken cancellationToken) => ValueTask.FromResult(commands);
    }

    private sealed class ThrowingProvider : ILauncherCommandProvider
    {
        public ValueTask<IReadOnlyList<ShellCommand>> GetCommandsAsync(
            string query,
            CancellationToken cancellationToken) => throw new InvalidOperationException("Provider failed.");
    }
}
