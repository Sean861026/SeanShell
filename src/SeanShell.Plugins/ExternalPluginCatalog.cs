using System.Security.Cryptography;
using System.Text.Json;
using SeanShell.PluginContracts;

namespace SeanShell.Plugins;

public sealed class ExternalPluginCatalog
{
    public const int MaximumPackageCount = 32;
    public const long MaximumManifestBytes = 64 * 1024;
    public const long MaximumEntryAssemblyBytes = 256 * 1024 * 1024;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _rootDirectory;
    private readonly IAuthenticodeVerifier _authenticodeVerifier;

    public ExternalPluginCatalog(string rootDirectory, IAuthenticodeVerifier authenticodeVerifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentNullException.ThrowIfNull(authenticodeVerifier);

        _rootDirectory = Path.GetFullPath(rootDirectory);
        _authenticodeVerifier = authenticodeVerifier;
    }

    public string RootDirectory => _rootDirectory;

    public async Task<IReadOnlyList<ExternalPluginCandidate>> ScanAsync(
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_rootDirectory))
        {
            return [];
        }

        var packageDirectories = Directory
            .EnumerateDirectories(_rootDirectory, "*", SearchOption.TopDirectoryOnly)
            .Order(StringComparer.OrdinalIgnoreCase)
            .Take(MaximumPackageCount)
            .ToArray();
        var candidates = new List<ExternalPluginCandidate>(packageDirectories.Length);

        foreach (var packageDirectory in packageDirectories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            candidates.Add(await InspectPackageAsync(packageDirectory, cancellationToken).ConfigureAwait(false));
        }

        var duplicateIds = candidates
            .Where(static candidate => !string.IsNullOrWhiteSpace(candidate.Id))
            .GroupBy(static candidate => candidate.Id!, StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return candidates
            .Select(candidate => candidate.Id is not null && duplicateIds.Contains(candidate.Id)
                ? candidate with
                {
                    Status = ExternalPluginCandidateStatus.InvalidManifest,
                    Detail = $"Plugin ID '{candidate.Id}' appears in more than one package.",
                }
                : candidate)
            .ToArray();
    }

    private async Task<ExternalPluginCandidate> InspectPackageAsync(
        string packageDirectory,
        CancellationToken cancellationToken)
    {
        var packageName = Path.GetFileName(packageDirectory);
        try
        {
            if (IsReparsePoint(packageDirectory))
            {
                return Invalid(
                    packageName,
                    ExternalPluginCandidateStatus.UnsafePath,
                    "Package directories that use reparse points are not scanned.");
            }

            var manifestPath = Path.Combine(packageDirectory, "plugin.json");
            if (!File.Exists(manifestPath))
            {
                return Invalid(packageName, ExternalPluginCandidateStatus.InvalidManifest, "plugin.json is missing.");
            }

            if (IsReparsePoint(manifestPath))
            {
                return Invalid(
                    packageName,
                    ExternalPluginCandidateStatus.UnsafePath,
                    "plugin.json may not use a reparse point.");
            }

            var manifestInfo = new FileInfo(manifestPath);
            if (manifestInfo.Length is <= 0 or > MaximumManifestBytes)
            {
                return Invalid(
                    packageName,
                    ExternalPluginCandidateStatus.InvalidManifest,
                    $"plugin.json must be between 1 and {MaximumManifestBytes / 1024} KiB.");
            }

            await using var manifestStream = new FileStream(
                manifestPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var manifest = await JsonSerializer.DeserializeAsync<ExternalManifest>(
                manifestStream,
                SerializerOptions,
                cancellationToken).ConfigureAwait(false);
            var validationError = ValidateManifest(manifest);
            if (validationError is not null)
            {
                return Invalid(packageName, ExternalPluginCandidateStatus.InvalidManifest, validationError);
            }

            var validated = manifest!;
            var assemblyPath = Path.GetFullPath(Path.Combine(packageDirectory, validated.EntryAssembly!));
            if (!IsInsideDirectory(packageDirectory, assemblyPath) ||
                !string.Equals(Path.GetExtension(assemblyPath), ".dll", StringComparison.OrdinalIgnoreCase))
            {
                return Candidate(
                    packageName,
                    validated,
                    ExternalPluginCandidateStatus.UnsafePath,
                    "Entry assembly must be a non-linked DLL contained by its package directory.");
            }

            if (!File.Exists(assemblyPath))
            {
                return Candidate(
                    packageName,
                    validated,
                    ExternalPluginCandidateStatus.MissingAssembly,
                    "The declared entry assembly does not exist.");
            }

            var assemblyInfo = new FileInfo(assemblyPath);
            if (assemblyInfo.Length is <= 0 or > MaximumEntryAssemblyBytes)
            {
                return Candidate(
                    packageName,
                    validated,
                    ExternalPluginCandidateStatus.InvalidManifest,
                    $"Entry assembly must be between 1 byte and {MaximumEntryAssemblyBytes / 1024 / 1024} MiB.");
            }

            if (HasReparsePoint(packageDirectory, assemblyPath))
            {
                return Candidate(
                    packageName,
                    validated,
                    ExternalPluginCandidateStatus.UnsafePath,
                    "Entry assembly must be a non-linked DLL contained by its package directory.");
            }

            var assemblyHash = await ComputeSha256Async(assemblyPath, cancellationToken).ConfigureAwait(false);
            var trust = _authenticodeVerifier.Verify(assemblyPath);
            if (!trust.IsTrusted)
            {
                var status = trust.Status switch
                {
                    AuthenticodeTrustStatus.Unsigned => ExternalPluginCandidateStatus.Unsigned,
                    AuthenticodeTrustStatus.Revoked => ExternalPluginCandidateStatus.RevokedSignature,
                    AuthenticodeTrustStatus.RevocationUnavailable => ExternalPluginCandidateStatus.RevocationUnavailable,
                    AuthenticodeTrustStatus.Expired => ExternalPluginCandidateStatus.ExpiredSignature,
                    AuthenticodeTrustStatus.ExplicitlyDistrusted => ExternalPluginCandidateStatus.ExplicitlyDistrusted,
                    _ => ExternalPluginCandidateStatus.UntrustedSignature,
                };
                return Candidate(
                    packageName,
                    validated,
                    status,
                    trust.Detail,
                    assemblyHash,
                    trust.SignerCertificateSha256,
                    trust.VerifiedAtUtc);
            }

            if (!string.Equals(
                    NormalizeHash(validated.PublisherCertificateSha256),
                    NormalizeHash(trust.SignerCertificateSha256),
                    StringComparison.OrdinalIgnoreCase))
            {
                return Candidate(
                    packageName,
                    validated,
                    ExternalPluginCandidateStatus.PublisherMismatch,
                    "The trusted signer's SHA-256 certificate fingerprint does not match the manifest.",
                    assemblyHash,
                    trust.SignerCertificateSha256,
                    trust.VerifiedAtUtc);
            }

            return Candidate(
                packageName,
                validated,
                ExternalPluginCandidateStatus.ReadyForConsent,
                "Signature and package checks passed. External loading remains disabled.",
                assemblyHash,
                trust.SignerCertificateSha256,
                trust.VerifiedAtUtc);
        }
        catch (JsonException exception)
        {
            return Invalid(packageName, ExternalPluginCandidateStatus.InvalidManifest, $"Invalid JSON: {exception.Message}");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CryptographicException)
        {
            return Invalid(packageName, ExternalPluginCandidateStatus.InvalidManifest, $"{exception.GetType().Name}: {exception.Message}");
        }
    }

    private static string? ValidateManifest(ExternalManifest? manifest)
    {
        if (manifest is null)
        {
            return "plugin.json does not contain a manifest.";
        }

        if (manifest.SchemaVersion != 1)
        {
            return $"Unsupported external manifest schema {manifest.SchemaVersion}.";
        }

        if (string.IsNullOrWhiteSpace(manifest.Id) ||
            manifest.Id.Any(static character => !(char.IsAsciiLetterOrDigit(character) || character is '.' or '-')))
        {
            return "Plugin IDs may contain only ASCII letters, digits, dots, and hyphens.";
        }

        if (string.IsNullOrWhiteSpace(manifest.Name) || string.IsNullOrWhiteSpace(manifest.Publisher))
        {
            return "Plugin name and publisher are required.";
        }

        if (!Version.TryParse(manifest.Version, out _))
        {
            return "Plugin version must use a numeric semantic version such as 1.0.0.";
        }

        if (manifest.MinimumHostApiVersion is <= 0 or > PluginHost.HostApiVersion)
        {
            return $"Minimum host API version must be between 1 and {PluginHost.HostApiVersion}.";
        }

        if (string.IsNullOrWhiteSpace(manifest.EntryAssembly))
        {
            return "Entry assembly is required.";
        }

        if (manifest.Capabilities is null ||
            manifest.Capabilities.Any(static capability =>
                !string.Equals(capability, "LauncherCommands", StringComparison.Ordinal) &&
                !string.Equals(capability, "BackgroundWork", StringComparison.Ordinal)))
        {
            return "Capabilities may contain only LauncherCommands and BackgroundWork.";
        }

        var normalizedPublisherHash = NormalizeHash(manifest.PublisherCertificateSha256);
        if (normalizedPublisherHash is null ||
            normalizedPublisherHash.Length != 64 ||
            normalizedPublisherHash.Any(static character => !char.IsAsciiHexDigit(character)))
        {
            return "PublisherCertificateSha256 must be a 64-character SHA-256 certificate fingerprint.";
        }

        return null;
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }

    private static bool IsInsideDirectory(string directory, string path)
    {
        var directoryPrefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)) +
                              Path.DirectorySeparatorChar;
        return path.StartsWith(directoryPrefix, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasReparsePoint(string packageDirectory, string assemblyPath)
    {
        if (IsReparsePoint(assemblyPath))
        {
            return true;
        }

        var packageRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(packageDirectory));
        for (var current = Path.GetDirectoryName(assemblyPath);
             current is not null && !string.Equals(current, packageRoot, StringComparison.OrdinalIgnoreCase);
             current = Path.GetDirectoryName(current))
        {
            if (Directory.Exists(current) && IsReparsePoint(current))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsReparsePoint(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static string? NormalizeHash(string? hash) =>
        string.IsNullOrWhiteSpace(hash)
            ? null
            : hash.Replace(":", string.Empty, StringComparison.Ordinal)
                .Replace(" ", string.Empty, StringComparison.Ordinal)
                .Trim();

    private static ExternalPluginCandidate Candidate(
        string packageName,
        ExternalManifest manifest,
        ExternalPluginCandidateStatus status,
        string detail,
        string? assemblyHash = null,
        string? signerHash = null,
        DateTimeOffset? trustVerifiedAtUtc = null) =>
        new(
            packageName,
            manifest.Id,
            manifest.Name!,
            manifest.Version,
            manifest.Publisher,
            manifest.EntryAssembly,
            ParseCapabilities(manifest.Capabilities!),
            status,
            detail,
            assemblyHash,
            signerHash,
            trustVerifiedAtUtc);

    private static ExternalPluginCandidate Invalid(
        string packageName,
        ExternalPluginCandidateStatus status,
        string detail) =>
        new(packageName, null, packageName, null, null, null, PluginCapability.None, status, detail);

    private static PluginCapability ParseCapabilities(IEnumerable<string> capabilities) =>
        capabilities.Aggregate(
            PluginCapability.None,
            static (value, capability) => value | (capability switch
            {
                "LauncherCommands" => PluginCapability.LauncherCommands,
                "BackgroundWork" => PluginCapability.BackgroundWork,
                _ => PluginCapability.None,
            }));

    private sealed record ExternalManifest(
        int SchemaVersion,
        string? Id,
        string? Name,
        string? Version,
        int MinimumHostApiVersion,
        string? Publisher,
        string[]? Capabilities,
        string? EntryAssembly,
        string? PublisherCertificateSha256);
}
