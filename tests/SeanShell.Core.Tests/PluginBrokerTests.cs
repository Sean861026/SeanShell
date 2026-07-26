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
    public async Task SessionProbesMatchingMetadataWithoutLoadingAssembly()
    {
        using var package = new TemporaryBrokerPackage();
        var now = DateTimeOffset.UtcNow;
        var request = CreateProbeRequest(package, now);
        using var input = new StringReader(PluginBrokerProtocol.Serialize(request));
        using var output = new StringWriter();

        var response = await PluginBrokerSession.RunAsync(
            input,
            output,
            processId: 123,
            currentTimeUtc: now);

        Assert.IsTrue(response.Accepted);
        Assert.IsNotNull(response.Metadata);
        Assert.AreEqual(request.Grant!.PluginId, response.Metadata.PluginId);
        Assert.AreEqual(request.Grant.AssemblySha256, response.Metadata.AssemblySha256);
        StringAssert.Contains(response.Status, "activation remains disabled");
    }

    [TestMethod]
    public async Task SessionRejectsExpiredCapabilityGrant()
    {
        using var package = new TemporaryBrokerPackage();
        var issuedAt = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(1);
        var original = CreateProbeRequest(package, issuedAt);
        var request = original with
        {
            Grant = original.Grant! with
            {
                ExpiresAtUtc = issuedAt + TimeSpan.FromSeconds(15),
            },
        };
        using var input = new StringReader(PluginBrokerProtocol.Serialize(request));
        using var output = new StringWriter();

        var response = await PluginBrokerSession.RunAsync(
            input,
            output,
            processId: 123,
            currentTimeUtc: DateTimeOffset.UtcNow);

        Assert.IsFalse(response.Accepted);
        StringAssert.Contains(response.Status, "expired");
    }

    [TestMethod]
    public async Task SessionRejectsAssemblyChangedAfterGrant()
    {
        using var package = new TemporaryBrokerPackage();
        var now = DateTimeOffset.UtcNow;
        var request = CreateProbeRequest(package, now);
        await File.AppendAllTextAsync(package.AssemblyPath, "changed");
        using var input = new StringReader(PluginBrokerProtocol.Serialize(request));
        using var output = new StringWriter();

        var response = await PluginBrokerSession.RunAsync(
            input,
            output,
            processId: 123,
            currentTimeUtc: now);

        Assert.IsFalse(response.Accepted);
        StringAssert.Contains(response.Status, "changed");
    }

    [TestMethod]
    public async Task SessionRejectsOversizedFrame()
    {
        using var input = new StringReader(
            new string('x', PluginBrokerProtocol.MaximumFrameCharacters + 1));
        using var output = new StringWriter();

        var response = await PluginBrokerSession.RunAsync(input, output, processId: 123);

        Assert.IsFalse(response.Accepted);
        Assert.AreEqual("Rejected malformed or unsafe request.", response.Status);
    }

    [TestMethod]
    public async Task ClientCompletesHandshakeInSeparateProcess()
    {
        var brokerPath = FindBrokerExecutable();
        var client = new PluginBrokerClient(brokerPath);

        var response = await client.CheckHealthAsync();

        Assert.IsTrue(response.Accepted);
        Assert.AreNotEqual(Environment.ProcessId, response.BrokerProcessId);
        StringAssert.Contains(response.Status, "sandbox active");
    }

    [TestMethod]
    public async Task ClosingBrokerJobTerminatesBlockedProcess()
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
            using (var sandbox = BrokerProcessSandbox.Create())
            {
                sandbox.Assign(process);
            }

            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await process.WaitForExitAsync(cancellation.Token);
            Assert.IsTrue(process.HasExited);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
    }

    [TestMethod]
    public async Task SessionRejectsUnknownProtocolFields()
    {
        var requestId = PluginBrokerProtocol.CreateRequestId();
        using var input = new StringReader(
            $$"""{"protocolVersion":2,"requestId":"{{requestId}}","operation":"health","unexpected":true}""");
        using var output = new StringWriter();

        var response = await PluginBrokerSession.RunAsync(input, output, processId: 123);

        Assert.IsFalse(response.Accepted);
        Assert.AreEqual("Rejected malformed or unsafe request.", response.Status);
    }

    [TestMethod]
    public async Task SessionRejectsGrantWithUnknownCapability()
    {
        using var package = new TemporaryBrokerPackage();
        var now = DateTimeOffset.UtcNow;
        var original = CreateProbeRequest(package, now);
        var request = original with
        {
            Grant = original.Grant! with { GrantedCapabilities = 4 },
        };
        using var input = new StringReader(PluginBrokerProtocol.Serialize(request));
        using var output = new StringWriter();

        var response = await PluginBrokerSession.RunAsync(
            input,
            output,
            processId: 123,
            currentTimeUtc: now);

        Assert.IsFalse(response.Accepted);
        StringAssert.Contains(response.Status, "unsupported capabilities");
    }

    [TestMethod]
    public async Task SessionRejectsEntryAssemblyOutsideGrantedPackage()
    {
        using var package = new TemporaryBrokerPackage();
        var now = DateTimeOffset.UtcNow;
        var original = CreateProbeRequest(package, now);
        var outsidePath = Path.Combine(
            Path.GetDirectoryName(package.DirectoryPath)!,
            $"Outside.{Guid.NewGuid():N}.dll");
        await File.WriteAllBytesAsync(outsidePath, [1, 2, 3]);
        try
        {
            var request = original with
            {
                Grant = original.Grant! with { EntryAssemblyPath = outsidePath },
            };
            using var input = new StringReader(PluginBrokerProtocol.Serialize(request));
            using var output = new StringWriter();

            var response = await PluginBrokerSession.RunAsync(
                input,
                output,
                processId: 123,
                currentTimeUtc: now);

            Assert.IsFalse(response.Accepted);
            StringAssert.Contains(response.Status, "unavailable or unsafe");
        }
        finally
        {
            File.Delete(outsidePath);
        }
    }

    [TestMethod]
    public async Task ClientProbesMetadataInSeparateProcess()
    {
        using var package = new TemporaryBrokerPackage();
        var client = new PluginBrokerClient(FindBrokerExecutable());
        var request = CreateProbeRequest(package, DateTimeOffset.UtcNow);

        var response = await client.ProbeMetadataAsync(request.Grant!);

        Assert.IsTrue(response.Accepted);
        Assert.IsNotNull(response.Metadata);
        Assert.AreEqual(request.Grant!.PluginId, response.Metadata.PluginId);
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

    private static PluginBrokerRequest CreateProbeRequest(
        TemporaryBrokerPackage package,
        DateTimeOffset issuedAtUtc) =>
        new(
            PluginBrokerProtocol.CurrentVersion,
            PluginBrokerProtocol.CreateRequestId(),
            PluginBrokerProtocol.MetadataProbeOperation,
            new PluginBrokerGrant(
                "seanshell.test-probe",
                package.DirectoryPath,
                package.AssemblyPath,
                package.AssemblySha256,
                new string('A', 64),
                GrantedCapabilities: 1,
                issuedAtUtc,
                issuedAtUtc + TimeSpan.FromSeconds(15)));

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

    private sealed class TemporaryBrokerPackage : IDisposable
    {
        public TemporaryBrokerPackage()
        {
            DirectoryPath = Path.Combine(
                Path.GetTempPath(),
                "SeanShell.Broker.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DirectoryPath);
            AssemblyPath = Path.Combine(DirectoryPath, "Test.Plugin.dll");
            File.WriteAllBytes(AssemblyPath, [1, 2, 3, 4, 5, 6]);
            AssemblySha256 = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(AssemblyPath)));
        }

        public string DirectoryPath { get; }

        public string AssemblyPath { get; }

        public string AssemblySha256 { get; }

        public void Dispose()
        {
            var allowedRoot = Path.GetFullPath(
                Path.Combine(Path.GetTempPath(), "SeanShell.Broker.Tests")) +
                Path.DirectorySeparatorChar;
            var resolved = Path.GetFullPath(DirectoryPath);
            if (!resolved.StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Refusing to remove a broker test directory outside the test root.");
            }

            if (Directory.Exists(resolved))
            {
                Directory.Delete(resolved, recursive: true);
            }
        }
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
