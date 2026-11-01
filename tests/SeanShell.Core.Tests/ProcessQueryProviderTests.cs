using System.ComponentModel;
using System.Diagnostics;
using SeanShell.Plugin.Docker;
using SeanShell.Plugin.Git;
using SeanShell.Plugin.Wsl;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class ProcessQueryProviderTests
{
    [TestMethod]
    [DataRow("wsl")]
    [DataRow("docker")]
    [DataRow("git")]
    public async Task QueryAlwaysSuppliesFiniteDeadlineEvenWithoutCallerCancellation(string provider)
    {
        ProcessStartInfo? captured = null;
        var calls = 0;
        await QueryAsync(provider, (startInfo, timeout, token) =>
        {
            captured = startInfo;
            calls++;
            Assert.AreEqual(TimeSpan.FromSeconds(10), timeout);
            Assert.AreEqual(CancellationToken.None, token);
            return Task.FromResult(new ProcessExecutionResult(0, string.Empty, string.Empty));
        }, CancellationToken.None);

        Assert.AreEqual(1, calls);
        Assert.IsNotNull(captured);
        Assert.IsFalse(captured.UseShellExecute);
        Assert.IsTrue(captured.CreateNoWindow);
        Assert.IsTrue(captured.RedirectStandardOutput);
        Assert.IsTrue(captured.RedirectStandardError);
        var expectedArguments = provider switch
        {
            "wsl" => new[] { "--list", "--verbose" },
            "docker" => ["container", "ls", "--all", "--format", "{{json .}}"],
            _ => ["-C", RepositoryPath, "status", "--porcelain=v1", "--branch"],
        };
        CollectionAssert.AreEqual(expectedArguments, captured.ArgumentList.ToArray());
    }

    [TestMethod]
    [DataRow("wsl")]
    [DataRow("docker")]
    [DataRow("git")]
    public async Task TimeoutIsNotTurnedIntoSuccessfulEmptyCache(string provider)
    {
        var expected = new TimeoutException("The isolated query exceeded its deadline.");
        var actual = await Assert.ThrowsExactlyAsync<TimeoutException>(() => QueryAsync(
            provider,
            (_, _, _) => Task.FromException<ProcessExecutionResult>(expected),
            CancellationToken.None));

        Assert.AreSame(expected, actual);
    }

    [TestMethod]
    [DataRow("wsl")]
    [DataRow("docker")]
    [DataRow("git")]
    public async Task CallerCancellationIsNotTurnedIntoUnavailableState(string provider)
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var actual = await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => QueryAsync(
            provider,
            (_, _, token) => Task.FromException<ProcessExecutionResult>(new OperationCanceledException(token)),
            cancellation.Token));

        Assert.AreEqual(cancellation.Token, actual.CancellationToken);
    }

    [TestMethod]
    [DataRow("wsl")]
    [DataRow("docker")]
    [DataRow("git")]
    public async Task MissingExecutableKeepsExistingUnavailableFallback(string provider)
    {
        var result = await QueryAsync(provider,
            (_, _, _) => Task.FromException<ProcessExecutionResult>(new Win32Exception(2)),
            CancellationToken.None);

        switch (provider)
        {
            case "wsl":
                Assert.HasCount(0, (IReadOnlyList<WslDistributionSnapshot>)result!);
                break;
            case "docker":
                Assert.AreSame(DockerContainerQueryResult.CliUnavailable, result);
                break;
            default:
                Assert.IsNull(result);
                break;
        }
    }

    private static string RepositoryPath => Path.Combine(Path.GetTempPath(), "SeanShell-status-query");

    private static async Task<object?> QueryAsync(
        string provider,
        Func<ProcessStartInfo, TimeSpan, CancellationToken, Task<ProcessExecutionResult>> runAsync,
        CancellationToken cancellationToken) => provider switch
        {
            "wsl" => await new WslProcessDistributionProvider(runAsync).GetDistributionsAsync(cancellationToken),
            "docker" => await new DockerProcessContainerProvider(runAsync).GetContainersAsync(cancellationToken),
            _ => await new GitProcessRepositoryInspector(runAsync).InspectAsync(RepositoryPath, cancellationToken),
        };
}
