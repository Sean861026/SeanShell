using SeanShell.Plugins;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class ExternalPluginCatalogTests
{
    private const string PublisherHash =
        "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";

    [TestMethod]
    public async Task ScanAsync_MissingRoot_ReturnsEmpty()
    {
        var root = GetUnusedPath();
        var catalog = new ExternalPluginCatalog(root, new FakeVerifier(AuthenticodeTrustStatus.Trusted, PublisherHash));

        var candidates = await catalog.ScanAsync();

        Assert.IsEmpty(candidates);
    }

    [TestMethod]
    public async Task ScanAsync_TrustedMatchingPackage_IsDiagnosticOnlyReadyForConsent()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            CreatePackage(root, "trusted", "seanshell.sample", "Sample.dll", PublisherHash);
            var catalog = new ExternalPluginCatalog(root, new FakeVerifier(AuthenticodeTrustStatus.Trusted, PublisherHash));

            var candidates = await catalog.ScanAsync();
            Assert.HasCount(1, candidates);
            var candidate = candidates[0];

            Assert.AreEqual(ExternalPluginCandidateStatus.ReadyForConsent, candidate.Status);
            Assert.AreEqual("seanshell.sample", candidate.Id);
            Assert.IsNotNull(candidate.AssemblySha256);
            StringAssert.Contains(candidate.Detail, "loading remains disabled");
        }
        finally
        {
            DeleteTemporaryDirectory(root);
        }
    }

    [TestMethod]
    public async Task ScanAsync_UnsignedPackage_IsRejected()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            CreatePackage(root, "unsigned", "seanshell.unsigned", "Unsigned.dll", PublisherHash);
            var catalog = new ExternalPluginCatalog(root, new FakeVerifier(AuthenticodeTrustStatus.Unsigned, null));

            var candidates = await catalog.ScanAsync();
            Assert.HasCount(1, candidates);
            var candidate = candidates[0];

            Assert.AreEqual(ExternalPluginCandidateStatus.Unsigned, candidate.Status);
        }
        finally
        {
            DeleteTemporaryDirectory(root);
        }
    }

    [TestMethod]
    public async Task ScanAsync_EntryAssemblyTraversal_IsRejectedBeforeVerification()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var verifier = new FakeVerifier(AuthenticodeTrustStatus.Trusted, PublisherHash);
            CreatePackage(root, "traversal", "seanshell.traversal", @"..\Outside.dll", PublisherHash);
            File.WriteAllBytes(Path.Combine(root, "Outside.dll"), [1, 2, 3]);
            var catalog = new ExternalPluginCatalog(root, verifier);

            var candidates = await catalog.ScanAsync();
            Assert.HasCount(1, candidates);
            var candidate = candidates[0];

            Assert.AreEqual(ExternalPluginCandidateStatus.UnsafePath, candidate.Status);
            Assert.AreEqual(0, verifier.CallCount);
        }
        finally
        {
            DeleteTemporaryDirectory(root);
        }
    }

    [TestMethod]
    public async Task ScanAsync_SignerDoesNotMatchManifest_IsRejected()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            CreatePackage(root, "mismatch", "seanshell.mismatch", "Mismatch.dll", PublisherHash);
            var unexpectedHash = new string('A', 64);
            var catalog = new ExternalPluginCatalog(root, new FakeVerifier(AuthenticodeTrustStatus.Trusted, unexpectedHash));

            var candidates = await catalog.ScanAsync();
            Assert.HasCount(1, candidates);
            var candidate = candidates[0];

            Assert.AreEqual(ExternalPluginCandidateStatus.PublisherMismatch, candidate.Status);
        }
        finally
        {
            DeleteTemporaryDirectory(root);
        }
    }

    [TestMethod]
    public async Task ScanAsync_DuplicateIds_RejectsEveryDuplicate()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            CreatePackage(root, "first", "seanshell.duplicate", "First.dll", PublisherHash);
            CreatePackage(root, "second", "seanshell.duplicate", "Second.dll", PublisherHash);
            var catalog = new ExternalPluginCatalog(root, new FakeVerifier(AuthenticodeTrustStatus.Trusted, PublisherHash));

            var candidates = await catalog.ScanAsync();

            Assert.HasCount(2, candidates);
            Assert.IsTrue(candidates.All(
                static candidate => candidate.Status == ExternalPluginCandidateStatus.InvalidManifest));
        }
        finally
        {
            DeleteTemporaryDirectory(root);
        }
    }

    private static void CreatePackage(
        string root,
        string directoryName,
        string id,
        string entryAssembly,
        string publisherHash)
    {
        var package = Directory.CreateDirectory(Path.Combine(root, directoryName)).FullName;
        var manifest = $$"""
            {
              "schemaVersion": 1,
              "id": "{{id}}",
              "name": "Test plugin",
              "version": "0.1.0",
              "minimumHostApiVersion": 1,
              "publisher": "SeanShell tests",
              "capabilities": [ "LauncherCommands" ],
              "entryAssembly": "{{entryAssembly.Replace("\\", "\\\\", StringComparison.Ordinal)}}",
              "publisherCertificateSha256": "{{publisherHash}}"
            }
            """;
        File.WriteAllText(Path.Combine(package, "plugin.json"), manifest);

        var assemblyPath = Path.GetFullPath(Path.Combine(package, entryAssembly));
        if (assemblyPath.StartsWith(package, StringComparison.OrdinalIgnoreCase))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(assemblyPath)!);
            File.WriteAllBytes(assemblyPath, [1, 2, 3, 4]);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "SeanShell.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string GetUnusedPath() =>
        Path.Combine(Path.GetTempPath(), "SeanShell.Tests", Guid.NewGuid().ToString("N"));

    private static void DeleteTemporaryDirectory(string path)
    {
        var testRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "SeanShell.Tests")) +
                       Path.DirectorySeparatorChar;
        var resolved = Path.GetFullPath(path);
        if (!resolved.StartsWith(testRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Refusing to remove a directory outside the test root.");
        }

        if (Directory.Exists(resolved))
        {
            Directory.Delete(resolved, recursive: true);
        }
    }

    [TestMethod]
    [DataRow(AuthenticodeTrustStatus.Revoked, ExternalPluginCandidateStatus.RevokedSignature)]
    [DataRow(AuthenticodeTrustStatus.RevocationUnavailable, ExternalPluginCandidateStatus.RevocationUnavailable)]
    [DataRow(AuthenticodeTrustStatus.Expired, ExternalPluginCandidateStatus.ExpiredSignature)]
    [DataRow(AuthenticodeTrustStatus.ExplicitlyDistrusted, ExternalPluginCandidateStatus.ExplicitlyDistrusted)]
    [DataRow(AuthenticodeTrustStatus.Untrusted, ExternalPluginCandidateStatus.UntrustedSignature)]
    public async Task ScanAsync_UntrustedPublisherState_IsDiagnosedAndBlocked(
        AuthenticodeTrustStatus trustStatus,
        ExternalPluginCandidateStatus expectedStatus)
    {
        var root = CreateTemporaryDirectory();
        try
        {
            CreatePackage(root, "blocked", "seanshell.blocked", "Blocked.dll", PublisherHash);
            var catalog = new ExternalPluginCatalog(root, new FakeVerifier(trustStatus, PublisherHash));

            var candidates = await catalog.ScanAsync();
            Assert.HasCount(1, candidates);
            var candidate = candidates[0];

            Assert.AreEqual(expectedStatus, candidate.Status);
            Assert.IsNotNull(candidate.TrustVerifiedAtUtc);
        }
        finally
        {
            DeleteTemporaryDirectory(root);
        }
    }

    private sealed class FakeVerifier(AuthenticodeTrustStatus status, string? signerHash) : IAuthenticodeVerifier
    {
        public int CallCount { get; private set; }

        public AuthenticodeVerificationResult Verify(string filePath)
        {
            CallCount++;
            return new AuthenticodeVerificationResult(
                status,
                "Fake trust result.",
                signerHash,
                DateTimeOffset.UtcNow);
        }
    }
}
