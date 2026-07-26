using System.Text.Json;

namespace SeanShell.PluginBroker.Protocol;

public static class PluginBrokerProtocol
{
    public const int CurrentVersion = 1;
    public const int MaximumFrameCharacters = 64 * 1024;
    public const string HealthOperation = "health";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
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
