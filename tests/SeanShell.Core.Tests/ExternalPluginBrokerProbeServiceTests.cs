using SeanShell.PluginBroker.Protocol;
using SeanShell.Plugins;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class ExternalPluginBrokerProbeServiceTests
{
    private const string PublisherHash =
        "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";

    [TestMethod]
    public async Task ProbeAsync_RevalidatesApprovedCandidateBeforeBrokerProbe()
    {
        using var package = new TemporaryExternalPackage();
        var verifier = new MutableVerifier(AuthenticodeTrustStatus.Trusted);
        var catalog = new ExternalPluginCatalog(package.RootDirectory, verifier);
        var trustPath = Path.Combine(package.RootDirectory, "plugin-trust.json");
        var store = new ExternalPluginTrustStore(trustPath);
        var trust = new ExternalPluginTrustManager(store, store.Load());
        var candidate = (await catalog.ScanAsync()).Single();
        trust.Approve(candidate, DateTimeOffset.UtcNow);
        var service = new ExternalPluginBrokerProbeService(
            catalog,
            trust,
            new PluginBrokerClient(FindBrokerExecutable()));

        var response = await service.ProbeAsync(candidate.Id!);

        Assert.IsTrue(response.Accepted);
        Assert.IsNotNull(response.Metadata);
        Assert.AreEqual(candidate.Id, response.Metadata.PluginId);
        Assert.IsGreaterThanOrEqualTo(2, verifier.CallCount);
    }

    [TestMethod]
    public async Task ProbeAsync_RevokedAfterConsent_FailsBeforeBrokerLaunch()
    {
        using var package = new TemporaryExternalPackage();
        var verifier = new MutableVerifier(AuthenticodeTrustStatus.Trusted);
        var catalog = new ExternalPluginCatalog(package.RootDirectory, verifier);
        var trustPath = Path.Combine(package.RootDirectory, "plugin-trust.json");
        var store = new ExternalPluginTrustStore(trustPath);
        var trust = new ExternalPluginTrustManager(store, store.Load());
        var candidate = (await catalog.ScanAsync()).Single();
        trust.Approve(candidate, DateTimeOffset.UtcNow);
        verifier.Status = AuthenticodeTrustStatus.Revoked;
        var service = new ExternalPluginBrokerProbeService(
            catalog,
            trust,
            new PluginBrokerClient(Path.Combine(package.RootDirectory, "missing-broker.exe")));

        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => service.ProbeAsync(candidate.Id!));

        StringAssert.Contains(exception.Message, "fresh trust scan");
    }

    private static string FindBrokerExecutable()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = directory
            .AncestorsAndSelf()
            .Select(static item => item.Name)
            .First(static name => name is "Debug" or "Release");
        var repository = directory
            .AncestorsAndSelf()
            .First(static item => File.Exists(Path.Combine(item.FullName, "SeanShell.sln")));
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

    private sealed class MutableVerifier(AuthenticodeTrustStatus status) : IAuthenticodeVerifier
    {
        public AuthenticodeTrustStatus Status { get; set; } = status;

        public int CallCount { get; private set; }

        public AuthenticodeVerificationResult Verify(string filePath)
        {
            CallCount++;
            return new AuthenticodeVerificationResult(
                Status,
                "Mutable verifier result.",
                PublisherHash,
                DateTimeOffset.UtcNow);
        }
    }

    private sealed class TemporaryExternalPackage : IDisposable
    {
        public TemporaryExternalPackage()
        {
            RootDirectory = Path.Combine(
                Path.GetTempPath(),
                "SeanShell.Probe.Tests",
                Guid.NewGuid().ToString("N"));
            var packageDirectory = Path.Combine(RootDirectory, "sample");
            Directory.CreateDirectory(packageDirectory);
            File.WriteAllBytes(Path.Combine(packageDirectory, "Sample.Plugin.dll"), [1, 2, 3, 4]);
            File.WriteAllText(
                Path.Combine(packageDirectory, "plugin.json"),
                $$"""
                  {
                    "schemaVersion": 1,
                    "id": "seanshell.sample-probe",
                    "name": "Sample probe",
                    "version": "0.1.0",
                    "minimumHostApiVersion": 1,
                    "publisher": "SeanShell tests",
                    "capabilities": [ "LauncherCommands" ],
                    "entryAssembly": "Sample.Plugin.dll",
                    "publisherCertificateSha256": "{{PublisherHash}}"
                  }
                  """);
        }

        public string RootDirectory { get; }

        public void Dispose()
        {
            var allowedRoot = Path.GetFullPath(
                Path.Combine(Path.GetTempPath(), "SeanShell.Probe.Tests")) +
                Path.DirectorySeparatorChar;
            var resolved = Path.GetFullPath(RootDirectory);
            if (!resolved.StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Refusing to remove an external probe test directory outside the test root.");
            }

            if (Directory.Exists(resolved))
            {
                Directory.Delete(resolved, recursive: true);
            }
        }
    }
}
