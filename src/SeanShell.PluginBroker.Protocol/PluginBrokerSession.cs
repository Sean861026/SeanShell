using System.Security.Cryptography;
using System.Text.Json;

namespace SeanShell.PluginBroker.Protocol;

public static class PluginBrokerSession
{
    public static async Task<PluginBrokerResponse> RunAsync(
        TextReader input,
        TextWriter output,
        int processId,
        ReadOnlyMemory<byte> sessionKey,
        CancellationToken cancellationToken = default,
        DateTimeOffset? currentTimeUtc = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        if (sessionKey.Length != PluginBrokerAuthentication.SessionKeyBytes)
        {
            throw new ArgumentException(
                "The broker session key has an invalid length.",
                nameof(sessionKey));
        }

        PluginBrokerResponse response;
        try
        {
            var frame = await PluginBrokerProtocol.ReadFrameAsync(input, cancellationToken)
                .ConfigureAwait(false);
            var request = PluginBrokerProtocol.DeserializeRequest(frame);
            response = PluginBrokerAuthentication.VerifyRequest(request, sessionKey.Span)
                ? await HandleAsync(
                    request,
                    processId,
                    currentTimeUtc ?? DateTimeOffset.UtcNow,
                    cancellationToken).ConfigureAwait(false)
                : Reject(request, processId, "Request authentication failed.");
        }
        catch (Exception exception) when (
            exception is JsonException or
                InvalidDataException or
                EndOfStreamException or
                ArgumentException or
                IOException or
                UnauthorizedAccessException or
                CryptographicException)
        {
            response = new PluginBrokerResponse(
                PluginBrokerProtocol.CurrentVersion,
                string.Empty,
                false,
                "Rejected malformed or unsafe request.",
                processId);
        }

        response = PluginBrokerAuthentication.SignResponse(response, sessionKey.Span);
        await output.WriteLineAsync(PluginBrokerProtocol.Serialize(response)).ConfigureAwait(false);
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        return response;
    }

    private static async Task<PluginBrokerResponse> HandleAsync(
        PluginBrokerRequest request,
        int processId,
        DateTimeOffset currentTimeUtc,
        CancellationToken cancellationToken)
    {
        if (request.ProtocolVersion != PluginBrokerProtocol.CurrentVersion)
        {
            return Reject(
                request,
                processId,
                $"Unsupported protocol version {request.ProtocolVersion}.");
        }

        if (!PluginBrokerProtocol.IsValidRequestId(request.RequestId))
        {
            return Reject(request, processId, "Request ID must be a 32-character GUID.");
        }

        if (string.Equals(
                request.Operation,
                PluginBrokerProtocol.HealthOperation,
                StringComparison.Ordinal))
        {
            return request.Grant is null
                ? new PluginBrokerResponse(
                    PluginBrokerProtocol.CurrentVersion,
                    request.RequestId,
                    true,
                    "Broker sandbox active; external activation is disabled.",
                    processId,
                    SessionId: request.SessionId,
                    Nonce: request.Nonce)
                : Reject(request, processId, "Health requests may not include a capability grant.");
        }

        if (!string.Equals(
                request.Operation,
                PluginBrokerProtocol.MetadataProbeOperation,
                StringComparison.Ordinal))
        {
            return Reject(request, processId, "The requested operation is not enabled.");
        }

        var validationError = ValidateGrant(request.Grant, currentTimeUtc);
        if (validationError is not null)
        {
            return Reject(request, processId, validationError);
        }

        var grant = request.Grant!;
        var packageDirectory = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(grant.PackageDirectoryPath));
        var entryAssembly = Path.GetFullPath(grant.EntryAssemblyPath);
        if (!Directory.Exists(packageDirectory) ||
            !File.Exists(entryAssembly) ||
            !IsInsideDirectory(packageDirectory, entryAssembly) ||
            !string.Equals(Path.GetExtension(entryAssembly), ".dll", StringComparison.OrdinalIgnoreCase) ||
            HasReparsePoint(packageDirectory, entryAssembly))
        {
            return Reject(request, processId, "The granted package path is unavailable or unsafe.");
        }

        var assemblyInfo = new FileInfo(entryAssembly);
        if (assemblyInfo.Length is <= 0 or > PluginBrokerProtocol.MaximumEntryAssemblyBytes)
        {
            return Reject(request, processId, "The granted entry assembly size is outside the allowed bounds.");
        }

        var observedHash = await ComputeSha256Async(entryAssembly, cancellationToken)
            .ConfigureAwait(false);
        if (!string.Equals(
                observedHash,
                grant.AssemblySha256,
                StringComparison.OrdinalIgnoreCase))
        {
            return Reject(request, processId, "The entry assembly changed after host verification.");
        }

        return new PluginBrokerResponse(
            PluginBrokerProtocol.CurrentVersion,
            request.RequestId,
            true,
            "Package metadata matched the short-lived capability grant; activation remains disabled.",
            processId,
            new PluginBrokerMetadata(
                grant.PluginId,
                observedHash,
                grant.PublisherCertificateSha256.ToUpperInvariant(),
                grant.GrantedCapabilities),
            request.SessionId,
            request.Nonce);
    }

    private static string? ValidateGrant(PluginBrokerGrant? grant, DateTimeOffset currentTimeUtc)
    {
        if (grant is null)
        {
            return "Metadata probes require a capability grant.";
        }

        if (string.IsNullOrWhiteSpace(grant.PluginId) ||
            grant.PluginId.Any(static character =>
                !(char.IsAsciiLetterOrDigit(character) || character is '.' or '-')))
        {
            return "The grant contains an invalid plugin ID.";
        }

        if (!Path.IsPathFullyQualified(grant.PackageDirectoryPath) ||
            !Path.IsPathFullyQualified(grant.EntryAssemblyPath))
        {
            return "The grant paths must be absolute.";
        }

        if (!PluginBrokerProtocol.IsValidSha256(grant.AssemblySha256) ||
            !PluginBrokerProtocol.IsValidSha256(grant.PublisherCertificateSha256))
        {
            return "The grant contains an invalid SHA-256 fingerprint.";
        }

        if (grant.GrantedCapabilities <= 0 ||
            (grant.GrantedCapabilities & ~PluginBrokerProtocol.KnownCapabilityMask) != 0)
        {
            return "The grant contains unsupported capabilities.";
        }

        var lifetime = grant.ExpiresAtUtc - grant.IssuedAtUtc;
        if (grant.IssuedAtUtc == default ||
            grant.ExpiresAtUtc == default ||
            lifetime <= TimeSpan.Zero ||
            lifetime > PluginBrokerProtocol.MaximumGrantLifetime ||
            currentTimeUtc < grant.IssuedAtUtc ||
            currentTimeUtc > grant.ExpiresAtUtc)
        {
            return "The capability grant is expired or outside its allowed lifetime.";
        }

        return null;
    }

    private static bool IsInsideDirectory(string directory, string path)
    {
        var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)) +
                     Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasReparsePoint(string packageDirectory, string assemblyPath)
    {
        if ((File.GetAttributes(packageDirectory) & FileAttributes.ReparsePoint) != 0 ||
            (File.GetAttributes(assemblyPath) & FileAttributes.ReparsePoint) != 0)
        {
            return true;
        }

        for (var current = Path.GetDirectoryName(assemblyPath);
             current is not null &&
             !string.Equals(current, packageDirectory, StringComparison.OrdinalIgnoreCase);
             current = Path.GetDirectoryName(current))
        {
            if (Directory.Exists(current) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<string> ComputeSha256Async(
        string filePath,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }

    private static PluginBrokerResponse Reject(
        PluginBrokerRequest request,
        int processId,
        string status) =>
        new(
            PluginBrokerProtocol.CurrentVersion,
            PluginBrokerProtocol.IsValidRequestId(request.RequestId)
                ? request.RequestId
                : string.Empty,
            false,
            status,
            processId,
            SessionId: request.SessionId,
            Nonce: request.Nonce);
}
