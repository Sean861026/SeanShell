using SeanShell.PluginContracts;
using SeanShell.Plugins;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class ExternalPluginTrustStoreTests
{
    private const string PublisherHash =
        "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";

    [TestMethod]
    public void ApprovePersistsExactPublisherAndCapabilities()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "plugin-trust.json");
        var store = new ExternalPluginTrustStore(path);
        var manager = new ExternalPluginTrustManager(store, store.Load());
        var grantedAt = new DateTimeOffset(2026, 7, 26, 3, 0, 0, TimeSpan.Zero);
        var candidate = CreateCandidate(
            PluginCapability.LauncherCommands | PluginCapability.BackgroundWork);

        manager.Approve(candidate, grantedAt);

        var reloaded = new ExternalPluginTrustManager(store, store.Load());
        Assert.IsTrue(reloaded.IsApproved(candidate));
        Assert.HasCount(1, reloaded.Consents);
        var consent = reloaded.Consents[0];
        Assert.AreEqual(candidate.Id, consent.PluginId);
        Assert.AreEqual(PublisherHash, consent.PublisherCertificateSha256);
        Assert.AreEqual(candidate.Capabilities, consent.GrantedCapabilities);
        Assert.AreEqual(grantedAt, consent.GrantedAtUtc);
        Assert.AreEqual(candidate.EntryType, consent.EntryType);
    }

    [TestMethod]
    public void ExpandedCapabilitiesRequireNewConsent()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "plugin-trust.json");
        var store = new ExternalPluginTrustStore(path);
        var manager = new ExternalPluginTrustManager(store, store.Load());
        manager.Approve(CreateCandidate(PluginCapability.LauncherCommands), DateTimeOffset.UtcNow);

        var expanded = CreateCandidate(
            PluginCapability.LauncherCommands | PluginCapability.BackgroundWork);

        Assert.IsFalse(manager.IsApproved(expanded));
    }

    [TestMethod]
    public void ChangedPublisherCertificateRequiresNewConsent()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "plugin-trust.json");
        var store = new ExternalPluginTrustStore(path);
        var manager = new ExternalPluginTrustManager(store, store.Load());
        var candidate = CreateCandidate(PluginCapability.LauncherCommands);
        manager.Approve(candidate, DateTimeOffset.UtcNow);

        var changedPublisher = candidate with
        {
            SignerCertificateSha256 = new string('A', 64),
        };

        Assert.IsFalse(manager.IsApproved(changedPublisher));
    }

    [TestMethod]
    public void ChangedEntryTypeRequiresNewConsent()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "plugin-trust.json");
        var store = new ExternalPluginTrustStore(path);
        var manager = new ExternalPluginTrustManager(store, store.Load());
        var candidate = CreateCandidate(PluginCapability.LauncherCommands);
        manager.Approve(candidate, DateTimeOffset.UtcNow);

        Assert.IsFalse(manager.IsApproved(candidate with
        {
            EntryType = "SeanShell.OtherPlugin",
        }));
    }

    [TestMethod]
    public void SchemaOneConsentMigratesWithoutActivationEntryType()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "plugin-trust.json");
        File.WriteAllText(path, $$"""
            {
              "schemaVersion": 1,
              "consents": [
                {
                  "pluginId": "seanshell.sample",
                  "publisherCertificateSha256": "{{PublisherHash}}",
                  "grantedCapabilities": "launcherCommands",
                  "grantedAtUtc": "2026-07-26T03:00:00+00:00"
                }
              ]
            }
            """);
        var store = new ExternalPluginTrustStore(path);

        var loaded = store.Load();

        Assert.AreEqual(
            ExternalPluginTrustDocument.CurrentSchemaVersion,
            loaded.Document.SchemaVersion);
        Assert.HasCount(1, loaded.Document.EffectiveConsents);
        Assert.IsNull(loaded.Document.EffectiveConsents[0].EntryType);
    }

    [TestMethod]
    public void RevokePersistsRemoval()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "plugin-trust.json");
        var store = new ExternalPluginTrustStore(path);
        var manager = new ExternalPluginTrustManager(store, store.Load());
        var candidate = CreateCandidate(PluginCapability.LauncherCommands);
        manager.Approve(candidate, DateTimeOffset.UtcNow);

        manager.Revoke(candidate);

        var reloaded = new ExternalPluginTrustManager(store, store.Load());
        Assert.IsFalse(reloaded.IsApproved(candidate));
        Assert.IsEmpty(reloaded.Consents);
    }

    [TestMethod]
    public void RevokeAllDoesNotRequireCandidatePackageToRemain()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "plugin-trust.json");
        var store = new ExternalPluginTrustStore(path);
        var manager = new ExternalPluginTrustManager(store, store.Load());
        manager.Approve(
            CreateCandidate(PluginCapability.LauncherCommands),
            DateTimeOffset.UtcNow);

        manager.RevokeAll();

        Assert.IsEmpty(manager.Consents);
        Assert.IsEmpty(store.Load().Document.EffectiveConsents);
    }

    [TestMethod]
    public void ApproveRejectsCandidateThatDidNotPassTrustChecks()
    {
        using var directory = new TemporaryDirectory();
        var store = new ExternalPluginTrustStore(Path.Combine(directory.Path, "plugin-trust.json"));
        var manager = new ExternalPluginTrustManager(store, store.Load());
        var unsigned = CreateCandidate(PluginCapability.LauncherCommands) with
        {
            Status = ExternalPluginCandidateStatus.Unsigned,
            SignerCertificateSha256 = null,
        };

        Assert.ThrowsExactly<InvalidOperationException>(
            () => manager.Approve(unsigned, DateTimeOffset.UtcNow));
        Assert.IsEmpty(manager.Consents);
    }

    [TestMethod]
    public void FailedSaveDoesNotApplyConsentInMemory()
    {
        using var directory = new TemporaryDirectory();
        var blockingFile = Path.Combine(directory.Path, "not-a-directory");
        File.WriteAllText(blockingFile, "blocked");
        var store = new ExternalPluginTrustStore(
            Path.Combine(blockingFile, "plugin-trust.json"));
        var manager = new ExternalPluginTrustManager(store, store.Load());

        Assert.ThrowsExactly<IOException>(
            () => manager.Approve(
                CreateCandidate(PluginCapability.LauncherCommands),
                DateTimeOffset.UtcNow));
        Assert.IsEmpty(manager.Consents);
    }

    [TestMethod]
    public void LoadRecoversLastKnownGoodTrustDocument()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "plugin-trust.json");
        var store = new ExternalPluginTrustStore(path);
        var manager = new ExternalPluginTrustManager(store, store.Load());
        var candidate = CreateCandidate(PluginCapability.LauncherCommands);
        manager.Approve(candidate, DateTimeOffset.UtcNow);
        File.WriteAllText(path, "{ damaged");

        var recovered = store.Load();

        Assert.IsTrue(recovered.WasRecovered);
        Assert.IsNotNull(recovered.Warning);
        Assert.HasCount(1, recovered.Document.EffectiveConsents);
    }

    private static ExternalPluginCandidate CreateCandidate(PluginCapability capabilities) =>
        new(
            "sample",
            "seanshell.sample",
            "Sample plugin",
            "0.1.0",
            "SeanShell tests",
            "Sample.dll",
            capabilities,
            ExternalPluginCandidateStatus.ReadyForConsent,
            "Trust checks passed.",
            new string('B', 64),
            PublisherHash)
        {
            EntryType = "SeanShell.SamplePlugin",
        };

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"SeanShell.Tests.{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
