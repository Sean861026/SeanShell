using System.Diagnostics;
using SeanShell.PluginBroker.Protocol;
using SeanShell.Plugins;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class PluginBrokerTests
{
    [TestMethod]
    public async Task SessionAcceptsOnlyVersionedHealthHandshake()
    {
        var request = new PluginBrokerRequest(
            PluginBrokerProtocol.CurrentVersion,
            PluginBrokerProtocol.CreateRequestId(),
            PluginBrokerProtocol.HealthOperation);
        using var input = new StringReader(PluginBrokerProtocol.Serialize(request));
        using var output = new StringWriter();

        var response = await PluginBrokerSession.RunAsync(input, output, processId: 123);

        Assert.IsTrue(response.Accepted);
        Assert.AreEqual(request.RequestId, response.RequestId);
        Assert.AreEqual(123, response.BrokerProcessId);
        StringAssert.Contains(response.Status, "activation is disabled");
    }

    [TestMethod]
    public async Task SessionRejectsActivationOperation()
    {
        var request = new PluginBrokerRequest(
            PluginBrokerProtocol.CurrentVersion,
            PluginBrokerProtocol.CreateRequestId(),
            "activate");
        using var input = new StringReader(PluginBrokerProtocol.Serialize(request));
        using var output = new StringWriter();

        var response = await PluginBrokerSession.RunAsync(input, output, processId: 123);

        Assert.IsFalse(response.Accepted);
        StringAssert.Contains(response.Status, "not enabled");
    }

    [TestMethod]
    public async Task SessionRejectsOversizedFrame()
    {
        using var input = new StringReader(
            new string('x', PluginBrokerProtocol.MaximumFrameCharacters + 1));
        using var output = new StringWriter();

        var response = await PluginBrokerSession.RunAsync(input, output, processId: 123);

        Assert.IsFalse(response.Accepted);
        StringAssert.Contains(response.Status, "may not exceed");
    }

    [TestMethod]
    public async Task ClientCompletesHandshakeInSeparateProcess()
    {
        var brokerPath = FindBrokerExecutable();
        var client = new PluginBrokerClient(brokerPath);

        var response = await client.CheckHealthAsync();

        Assert.IsTrue(response.Accepted);
        Assert.AreNotEqual(Environment.ProcessId, response.BrokerProcessId);
    }

    [TestMethod]
    public async Task BrokerProcessRejectsUnknownOperationAndExits()
    {
        var request = new PluginBrokerRequest(
            PluginBrokerProtocol.CurrentVersion,
            PluginBrokerProtocol.CreateRequestId(),
            "load-assembly");

        var result = await RunBrokerAsync(request);

        Assert.AreEqual(2, result.ExitCode);
        Assert.IsFalse(result.Response.Accepted);
        StringAssert.Contains(result.Response.Status, "not enabled");
    }

    [TestMethod]
    public async Task ClientFailsBeforeLaunchWhenBrokerIsMissing()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"SeanShell.PluginBroker.Missing.{Guid.NewGuid():N}.exe");
        var client = new PluginBrokerClient(path);

        await Assert.ThrowsExactlyAsync<FileNotFoundException>(
            () => client.CheckHealthAsync());
    }

    private static async Task<(int ExitCode, PluginBrokerResponse Response)> RunBrokerAsync(
        PluginBrokerRequest request)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(FindBrokerExecutable())
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
            },
        };
        Assert.IsTrue(process.Start());
        try
        {
            await process.StandardInput.WriteLineAsync(PluginBrokerProtocol.Serialize(request));
            process.StandardInput.Close();
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var frame = await PluginBrokerProtocol.ReadFrameAsync(
                process.StandardOutput,
                cancellation.Token);
            await process.WaitForExitAsync(cancellation.Token);
            return (process.ExitCode, PluginBrokerProtocol.DeserializeResponse(frame));
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
    }

    private static string FindBrokerExecutable()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = directory
            .AncestorsAndSelf()
            .Select(static item => item.Name)
            .FirstOrDefault(static name => name is "Debug" or "Release")
            ?? throw new InvalidOperationException("The test configuration directory was not found.");
        var repository = directory
            .AncestorsAndSelf()
            .FirstOrDefault(static item => File.Exists(Path.Combine(item.FullName, "SeanShell.sln")))
            ?? throw new InvalidOperationException("The repository root was not found.");
        var path = Path.Combine(
            repository.FullName,
            "src",
            "SeanShell.PluginBroker",
            "bin",
            configuration,
            "net10.0",
            "SeanShell.PluginBroker.exe");
        Assert.IsTrue(File.Exists(path), $"Broker executable not found: {path}");
        return path;
    }
}

internal static class DirectoryInfoExtensions
{
    public static IEnumerable<DirectoryInfo> AncestorsAndSelf(this DirectoryInfo directory)
    {
        for (var current = directory; current is not null; current = current.Parent)
        {
            yield return current;
        }
    }
}
