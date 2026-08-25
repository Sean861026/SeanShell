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
    public async Task ScanAsync_SchemaTwoCapturesBoundedEntryType()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            CreatePackage(root, "schema-two", "seanshell.v2", "Plugin.dll", PublisherHash);
            var package = Path.Combine(root, "schema-two");
            SetSchemaTwoEntryType(package, "Example.Publisher.LauncherPlugin");
            var catalog = new ExternalPluginCatalog(
                root,
                new FakeVerifier(AuthenticodeTrustStatus.Trusted, PublisherHash));

            var candidate = (await catalog.ScanAsync()).Single();

            Assert.AreEqual(ExternalPluginCandidateStatus.ReadyForConsent, candidate.Status);
            Assert.AreEqual("Example.Publisher.LauncherPlugin", candidate.EntryType);
        }
        finally
        {
            DeleteTemporaryDirectory(root);
        }
    }

    [TestMethod]
    public async Task ScanAsync_SchemaTwoWithoutEntryTypeIsRejected()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            CreatePackage(root, "missing-entry-type", "seanshell.v2-missing", "Plugin.dll", PublisherHash);
            var package = Path.Combine(root, "missing-entry-type");
            var manifestPath = Path.Combine(package, "plugin.json");
            File.WriteAllText(
                manifestPath,
                File.ReadAllText(manifestPath).Replace(
                    "\"schemaVersion\": 1",
                    "\"schemaVersion\": 2",
                    StringComparison.Ordinal));
            var catalog = new ExternalPluginCatalog(
                root,
                new FakeVerifier(AuthenticodeTrustStatus.Trusted, PublisherHash));

            var candidate = (await catalog.ScanAsync()).Single();

            Assert.AreEqual(ExternalPluginCandidateStatus.InvalidManifest, candidate.Status);
            StringAssert.Contains(candidate.Detail, "EntryType");
        }
        finally
        {
            DeleteTemporaryDirectory(root);
        }
    }

    [TestMethod]
    public async Task ScanAsync_TrustedSamePublisherDependencies_AreBounded()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            CreatePackage(root, "dependencies", "seanshell.dependencies", "Plugin.dll", PublisherHash);
            var package = Path.Combine(root, "dependencies");
            var dependencyPath = Path.Combine(package, "lib", "Support.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(dependencyPath)!);
            File.WriteAllBytes(dependencyPath, [5, 6, 7, 8]);
            SetDependencies(
                package,
                $$"""[{"path":"lib/Support.dll","sha256":"{{ComputeHash(dependencyPath)}}","kind":"managed"}]""");
            var verifier = new FakeVerifier(AuthenticodeTrustStatus.Trusted, PublisherHash);
            var catalog = new ExternalPluginCatalog(root, verifier);

            var candidate = (await catalog.ScanAsync()).Single();

            Assert.AreEqual(ExternalPluginCandidateStatus.ReadyForConsent, candidate.Status);
            Assert.HasCount(1, candidate.Dependencies!);
            Assert.AreEqual("lib\\Support.dll", candidate.Dependencies![0].RelativePath);
            Assert.AreEqual(2, verifier.CallCount);
        }
        finally
        {
            DeleteTemporaryDirectory(root);
        }
    }

    [TestMethod]
    public async Task ScanAsync_DependencyHashMismatch_IsRejectedBeforeDependencyTrust()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            CreatePackage(root, "changed-dependency", "seanshell.changed", "Plugin.dll", PublisherHash);
            var package = Path.Combine(root, "changed-dependency");
            File.WriteAllBytes(Path.Combine(package, "Support.dll"), [5, 6, 7, 8]);
            SetDependencies(
                package,
                $$"""[{"path":"Support.dll","sha256":"{{new string('A', 64)}}","kind":"managed"}]""");
            var verifier = new FakeVerifier(AuthenticodeTrustStatus.Trusted, PublisherHash);
            var catalog = new ExternalPluginCatalog(root, verifier);

            var candidate = (await catalog.ScanAsync()).Single();

            Assert.AreEqual(ExternalPluginCandidateStatus.InvalidManifest, candidate.Status);
            StringAssert.Contains(candidate.Detail, "does not match");
            Assert.AreEqual(1, verifier.CallCount);
        }
        finally
        {
            DeleteTemporaryDirectory(root);
        }
    }

    [TestMethod]
    public async Task ScanAsync_DependencyFromDifferentPublisher_IsRejected()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            CreatePackage(root, "foreign-dependency", "seanshell.foreign", "Plugin.dll", PublisherHash);
            var package = Path.Combine(root, "foreign-dependency");
            var dependencyPath = Path.Combine(package, "Support.dll");
            File.WriteAllBytes(dependencyPath, [5, 6, 7, 8]);
            SetDependencies(
                package,
                $$"""[{"path":"Support.dll","sha256":"{{ComputeHash(dependencyPath)}}","kind":"native"}]""");
            var verifier = new PathVerifier();
            var catalog = new ExternalPluginCatalog(root, verifier);

            var candidate = (await catalog.ScanAsync()).Single();

            Assert.AreEqual(ExternalPluginCandidateStatus.UntrustedSignature, candidate.Status);
            StringAssert.Contains(candidate.Detail, "publisher certificate");
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

    private static string ComputeHash(string path) =>
        Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));

    private static void SetDependencies(string packageDirectory, string dependenciesJson)
    {
        var manifestPath = Path.Combine(packageDirectory, "plugin.json");
        var manifest = File.ReadAllText(manifestPath);
        manifest = manifest.Replace(
            "\"publisherCertificateSha256\":",
            $"\"dependencies\": {dependenciesJson},\r\n  \"publisherCertificateSha256\":",
            StringComparison.Ordinal);
        File.WriteAllText(manifestPath, manifest);
    }

    private static void SetSchemaTwoEntryType(
        string packageDirectory,
        string entryType)
    {
        var manifestPath = Path.Combine(packageDirectory, "plugin.json");
        var manifest = File.ReadAllText(manifestPath)
            .Replace(
                "\"schemaVersion\": 1",
                "\"schemaVersion\": 2",
                StringComparison.Ordinal)
            .Replace(
                "\"publisherCertificateSha256\":",
                $"\"entryType\": \"{entryType}\",\r\n  \"publisherCertificateSha256\":",
                StringComparison.Ordinal);
        File.WriteAllText(manifestPath, manifest);
    }

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

    private sealed class PathVerifier : IAuthenticodeVerifier
    {
        public AuthenticodeVerificationResult Verify(string filePath) =>
            new(
                AuthenticodeTrustStatus.Trusted,
                "Fake trust result.",
                filePath.EndsWith("Support.dll", StringComparison.OrdinalIgnoreCase)
                    ? new string('B', 64)
                    : PublisherHash,
                DateTimeOffset.UtcNow);
    }
}
