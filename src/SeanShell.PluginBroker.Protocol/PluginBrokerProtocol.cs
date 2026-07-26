using System.Text.Json;
using System.Text.Json.Serialization;

namespace SeanShell.PluginBroker.Protocol;

public static class PluginBrokerProtocol
{
    public const int CurrentVersion = 2;
    public const int MaximumFrameCharacters = 64 * 1024;
    public const long MaximumEntryAssemblyBytes = 256 * 1024 * 1024;
    public const int KnownCapabilityMask = 3;
    public const string HealthOperation = "health";
    public const string MetadataProbeOperation = "probe-metadata";
    public static readonly TimeSpan MaximumGrantLifetime = TimeSpan.FromSeconds(30);

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static string Serialize(PluginBrokerRequest request) =>
        JsonSerializer.Serialize(request, SerializerOptions);

    public static string Serialize(PluginBrokerResponse response) =>
        JsonSerializer.Serialize(response, SerializerOptions);

    public static PluginBrokerRequest DeserializeRequest(string frame)
    {
        ValidateFrame(frame);
        return JsonSerializer.Deserialize<PluginBrokerRequest>(frame, SerializerOptions)
            ?? throw new InvalidDataException("The broker request is empty.");
    }

    public static PluginBrokerResponse DeserializeResponse(string frame)
    {
        ValidateFrame(frame);
        return JsonSerializer.Deserialize<PluginBrokerResponse>(frame, SerializerOptions)
            ?? throw new InvalidDataException("The broker response is empty.");
    }

    public static async Task<string> ReadFrameAsync(
        TextReader reader,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var buffer = new char[1];
        var characters = new List<char>();
        while (true)
        {
            var read = await reader.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0 || buffer[0] == '\n')
            {
                break;
            }

            if (buffer[0] != '\r')
            {
                characters.Add(buffer[0]);
            }

            if (characters.Count > MaximumFrameCharacters)
            {
                throw new InvalidDataException(
                    $"Broker frames may not exceed {MaximumFrameCharacters} characters.");
            }
        }

        if (characters.Count == 0)
        {
            throw new EndOfStreamException("The broker stream ended before a frame was received.");
        }

        return new string([.. characters]);
    }

    public static string CreateRequestId() => Guid.NewGuid().ToString("N");

    public static bool IsValidRequestId(string? requestId) =>
        Guid.TryParseExact(requestId, "N", out _);

    public static bool IsValidSha256(string? value) =>
        value is { Length: 64 } &&
        value.All(static character => char.IsAsciiHexDigit(character));

    private static void ValidateFrame(string frame)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(frame);
        if (frame.Length > MaximumFrameCharacters)
        {
            throw new InvalidDataException(
                $"Broker frames may not exceed {MaximumFrameCharacters} characters.");
        }
    }
}
