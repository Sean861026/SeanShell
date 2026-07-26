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
            new PluginBrokerClient(FindBrokerExecutable()),
            CreateQuarantineManager(package.RootDirectory));

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
            new PluginBrokerClient(Path.Combine(package.RootDirectory, "missing-broker.exe")),
            CreateQuarantineManager(package.RootDirectory));

        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => service.ProbeAsync(candidate.Id!));

        StringAssert.Contains(exception.Message, "fresh trust scan");
    }

    [TestMethod]
    public async Task ProbeAsync_RepeatedBrokerTimeoutsQuarantinePlugin()
    {
        using var package = new TemporaryExternalPackage();
        var setup = await CreateApprovedSetupAsync(package);
        var broker = new StubBroker(static (_, _) =>
            Task.FromException<PluginBrokerResponse>(
                new TimeoutException("Broker timed out.")));
        var quarantine = CreateQuarantineManager(package.RootDirectory);
        var service = new ExternalPluginBrokerProbeService(
            setup.Catalog,
            setup.Trust,
            broker,
            quarantine);

        for (var attempt = 0; attempt < PluginBrokerQuarantineManager.FailureThreshold; attempt++)
        {
            await Assert.ThrowsExactlyAsync<TimeoutException>(
                () => service.ProbeAsync(setup.Candidate.Id!));
        }

        var exception = await Assert.ThrowsExactlyAsync<PluginBrokerQuarantinedException>(
            () => service.ProbeAsync(setup.Candidate.Id!));
        Assert.AreEqual(setup.Candidate.Id, exception.PluginId);
        Assert.AreEqual(PluginBrokerQuarantineManager.FailureThreshold, broker.CallCount);
    }

    [TestMethod]
    public async Task ProbeAsync_SuccessClearsPriorFailureWindow()
    {
        using var package = new TemporaryExternalPackage();
        var setup = await CreateApprovedSetupAsync(package);
        var quarantine = CreateQuarantineManager(package.RootDirectory);
        quarantine.RecordFailure(setup.Candidate.Id!);
        quarantine.RecordFailure(setup.Candidate.Id!);
        var broker = new StubBroker((grant, _) =>
            Task.FromResult(CreateAcceptedResponse(grant)));
        var service = new ExternalPluginBrokerProbeService(
            setup.Catalog,
            setup.Trust,
            broker,
            quarantine);

        var response = await service.ProbeAsync(setup.Candidate.Id!);

        Assert.IsTrue(response.Accepted);
        Assert.IsEmpty(quarantine.Statuses);
    }

    [TestMethod]
    public async Task ProbeAsync_UserCancellationDoesNotCountAsBrokerFailure()
    {
        using var package = new TemporaryExternalPackage();
        var setup = await CreateApprovedSetupAsync(package);
        var quarantine = CreateQuarantineManager(package.RootDirectory);
        var broker = new StubBroker(
            static (_, _) => Task.FromCanceled<PluginBrokerResponse>(
                new CancellationToken(canceled: true)));
        var service = new ExternalPluginBrokerProbeService(
            setup.Catalog,
            setup.Trust,
            broker,
            quarantine);

        await Assert.ThrowsExactlyAsync<TaskCanceledException>(
            () => service.ProbeAsync(setup.Candidate.Id!));

        Assert.AreEqual(1, broker.CallCount);
        Assert.IsEmpty(quarantine.Statuses);
    }

    [TestMethod]
    public async Task ProbeAsync_ExpiredQuarantineAllowsSuccessfulRecovery()
    {
        using var package = new TemporaryExternalPackage();
        var setup = await CreateApprovedSetupAsync(package);
        var time = new ManualTimeProvider(
            new DateTimeOffset(2026, 7, 26, 8, 0, 0, TimeSpan.Zero));
        var quarantine = CreateQuarantineManager(package.RootDirectory, time);
        for (var attempt = 0; attempt < PluginBrokerQuarantineManager.FailureThreshold; attempt++)
        {
            quarantine.RecordFailure(setup.Candidate.Id!);
        }

        time.Advance(PluginBrokerQuarantineManager.QuarantineDuration + TimeSpan.FromSeconds(1));
        var broker = new StubBroker((grant, _) =>
            Task.FromResult(CreateAcceptedResponse(grant)));
        var service = new ExternalPluginBrokerProbeService(
            setup.Catalog,
            setup.Trust,
            broker,
            quarantine);

        var response = await service.ProbeAsync(setup.Candidate.Id!);

        Assert.IsTrue(response.Accepted);
        Assert.AreEqual(1, broker.CallCount);
        Assert.IsEmpty(quarantine.Statuses);
    }

    [TestMethod]
    public void QuarantinePersistsAcrossManagerRestart()
    {
        using var package = new TemporaryExternalPackage();
        var time = new ManualTimeProvider(
            new DateTimeOffset(2026, 7, 26, 8, 0, 0, TimeSpan.Zero));
        var manager = CreateQuarantineManager(package.RootDirectory, time);
        for (var attempt = 0; attempt < PluginBrokerQuarantineManager.FailureThreshold; attempt++)
        {
            manager.RecordFailure("seanshell.sample-probe");
        }

        var reloaded = CreateQuarantineManager(package.RootDirectory, time);

        Assert.ThrowsExactly<PluginBrokerQuarantinedException>(
            () => reloaded.EnsureProbeAllowed("seanshell.sample-probe"));
    }

    [TestMethod]
    public void DamagedQuarantineHistoryWithoutRecoveryCopyBlocksProbes()
    {
        using var package = new TemporaryExternalPackage();
        var path = GetQuarantinePath(package.RootDirectory);
        File.WriteAllText(path, "{ damaged");
        var store = new PluginBrokerQuarantineStore(path);
        var load = store.Load();
        var manager = new PluginBrokerQuarantineManager(store, load);

        var exception = Assert.ThrowsExactly<InvalidOperationException>(
            () => manager.EnsureProbeAllowed("seanshell.sample-probe"));

        Assert.IsFalse(load.PersistenceAvailable);
        StringAssert.Contains(exception.Message, "blocked");
    }

    [TestMethod]
    public async Task ProbeAsync_MissingBrokerDoesNotQuarantinePlugin()
    {
        using var package = new TemporaryExternalPackage();
        var setup = await CreateApprovedSetupAsync(package);
        var quarantine = CreateQuarantineManager(package.RootDirectory);
        var service = new ExternalPluginBrokerProbeService(
            setup.Catalog,
            setup.Trust,
            new PluginBrokerClient(
                Path.Combine(package.RootDirectory, "missing-broker.exe")),
            quarantine);

        await Assert.ThrowsExactlyAsync<FileNotFoundException>(
            () => service.ProbeAsync(setup.Candidate.Id!));

        Assert.IsEmpty(quarantine.Statuses);
    }

    [TestMethod]
    public void QuarantineStoreRecoversLastKnownGoodCopy()
    {
        using var package = new TemporaryExternalPackage();
        var manager = CreateQuarantineManager(package.RootDirectory);
        manager.RecordFailure("seanshell.sample-probe");
        manager.RecordFailure("seanshell.sample-probe");
        var path = GetQuarantinePath(package.RootDirectory);
        File.WriteAllText(path, "{ damaged");
        var store = new PluginBrokerQuarantineStore(path);

        var recovered = store.Load();

        Assert.IsTrue(recovered.PersistenceAvailable);
        Assert.IsTrue(recovered.WasRecovered);
        Assert.IsNotNull(recovered.Warning);
        Assert.HasCount(1, recovered.Document.EffectiveEntries);
        Assert.AreEqual(
            1,
            recovered.Document.EffectiveEntries[0].ConsecutiveFailures);
    }

    [TestMethod]
    public void FailureOutsideObservationWindowStartsNewSequence()
    {
        using var package = new TemporaryExternalPackage();
        var time = new ManualTimeProvider(
            new DateTimeOffset(2026, 7, 26, 8, 0, 0, TimeSpan.Zero));
        var manager = CreateQuarantineManager(package.RootDirectory, time);
        manager.RecordFailure("seanshell.sample-probe");
        manager.RecordFailure("seanshell.sample-probe");
        time.Advance(PluginBrokerQuarantineManager.FailureWindow + TimeSpan.FromSeconds(1));

        var status = manager.RecordFailure("seanshell.sample-probe");

        Assert.AreEqual(1, status.ConsecutiveFailures);
        Assert.IsNull(status.QuarantinedUntilUtc);
    }

    [TestMethod]
    public void FailedQuarantineSaveBlocksLaterProbes()
    {
        using var package = new TemporaryExternalPackage();
        var blockingFile = Path.Combine(package.RootDirectory, "not-a-directory");
        File.WriteAllText(blockingFile, "blocked");
        var store = new PluginBrokerQuarantineStore(
            Path.Combine(blockingFile, "plugin-broker-health.json"));
        var manager = new PluginBrokerQuarantineManager(store, store.Load());

        Assert.ThrowsExactly<InvalidOperationException>(
            () => manager.RecordFailure("seanshell.sample-probe"));
        var exception = Assert.ThrowsExactly<InvalidOperationException>(
            () => manager.EnsureProbeAllowed("seanshell.sample-probe"));
        StringAssert.Contains(exception.Message, "blocked");
    }

    private static async Task<ApprovedSetup> CreateApprovedSetupAsync(
        TemporaryExternalPackage package)
    {
        var verifier = new MutableVerifier(AuthenticodeTrustStatus.Trusted);
        var catalog = new ExternalPluginCatalog(package.RootDirectory, verifier);
        var store = new ExternalPluginTrustStore(
            Path.Combine(package.RootDirectory, "plugin-trust.json"));
        var trust = new ExternalPluginTrustManager(store, store.Load());
        var candidate = (await catalog.ScanAsync()).Single();
        trust.Approve(candidate, DateTimeOffset.UtcNow);
        return new ApprovedSetup(catalog, trust, candidate);
    }

    private static PluginBrokerQuarantineManager CreateQuarantineManager(
        string rootDirectory,
        TimeProvider? timeProvider = null)
    {
        var store = new PluginBrokerQuarantineStore(GetQuarantinePath(rootDirectory));
        return new PluginBrokerQuarantineManager(store, store.Load(), timeProvider);
    }

    private static string GetQuarantinePath(string rootDirectory) =>
        Path.Combine(rootDirectory, "plugin-broker-health.json");

    private static PluginBrokerResponse CreateAcceptedResponse(PluginBrokerGrant grant) =>
        new(
            PluginBrokerProtocol.CurrentVersion,
            PluginBrokerProtocol.CreateRequestId(),
            true,
            "accepted",
            123,
            new PluginBrokerMetadata(
                grant.PluginId,
                grant.AssemblySha256,
                grant.PublisherCertificateSha256,
                grant.GrantedCapabilities,
                grant.Dependencies?.Length ?? 0,
                PluginBrokerDependencySet.ComputeDigest(grant.Dependencies ?? [])));

    private static string FindBrokerExecutable()
    {
        var packagedExecutable =
            Environment.GetEnvironmentVariable("SEANSHELL_BROKER_TEST_EXECUTABLE");
        if (!string.IsNullOrWhiteSpace(packagedExecutable))
        {
            var resolved = Path.GetFullPath(packagedExecutable);
            Assert.IsTrue(
                File.Exists(resolved),
                $"Packaged broker executable not found: {resolved}");
            return resolved;
        }

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

    private sealed class StubBroker(
        Func<PluginBrokerGrant, CancellationToken, Task<PluginBrokerResponse>> handler)
        : IPluginBrokerProbeClient
    {
        public int CallCount { get; private set; }

        public Task<PluginBrokerResponse> ProbeMetadataAsync(
            PluginBrokerGrant grant,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return handler(grant, cancellationToken);
        }
    }

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration) => _utcNow += duration;
    }

    private sealed record ApprovedSetup(
        ExternalPluginCatalog Catalog,
        ExternalPluginTrustManager Trust,
        ExternalPluginCandidate Candidate);

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
