using System.Text.Json;
using System.Text.Json.Serialization;

namespace SeanShell.PluginBroker.Protocol;

public static class PluginBrokerCommandCodec
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static string Serialize(PluginBrokerCommandQuery value)
    {
        ThrowIfInvalid(PluginBrokerCommandContract.Validate(value));
        return SerializeCore(value);
    }

    public static string Serialize(PluginBrokerCommandDescriptor[] value)
    {
        ThrowIfInvalid(PluginBrokerCommandContract.Validate(value));
        return SerializeCore(value);
    }

    public static string Serialize(PluginBrokerCommandInvocation value)
    {
        ThrowIfInvalid(PluginBrokerCommandContract.Validate(value));
        return SerializeCore(value);
    }

    public static string Serialize(PluginBrokerCommandResult value)
    {
        ThrowIfInvalid(PluginBrokerCommandContract.Validate(value));
        return SerializeCore(value);
    }

    public static PluginBrokerCommandQuery DeserializeQuery(string frame)
    {
        var value = DeserializeCore<PluginBrokerCommandQuery>(frame);
        ThrowIfInvalid(PluginBrokerCommandContract.Validate(value));
        return value;
    }

    public static PluginBrokerCommandDescriptor[] DeserializeDescriptors(string frame)
    {
        var value = DeserializeCore<PluginBrokerCommandDescriptor[]>(frame);
        ThrowIfInvalid(PluginBrokerCommandContract.Validate(value));
        return value;
    }

    public static PluginBrokerCommandInvocation DeserializeInvocation(string frame)
    {
        var value = DeserializeCore<PluginBrokerCommandInvocation>(frame);
        ThrowIfInvalid(PluginBrokerCommandContract.Validate(value));
        return value;
    }

    public static PluginBrokerCommandResult DeserializeResult(string frame)
    {
        var value = DeserializeCore<PluginBrokerCommandResult>(frame);
        ThrowIfInvalid(PluginBrokerCommandContract.Validate(value));
        return value;
    }

    private static string SerializeCore<T>(T value)
    {
        var frame = JsonSerializer.Serialize(value, SerializerOptions);
        ValidateFrame(frame);
        return frame;
    }

    private static T DeserializeCore<T>(string frame)
    {
        ValidateFrame(frame);
        return JsonSerializer.Deserialize<T>(frame, SerializerOptions)
            ?? throw new InvalidDataException("The command payload is empty.");
    }

    private static void ValidateFrame(string? frame)
    {
        if (string.IsNullOrWhiteSpace(frame) ||
            frame.Length > PluginBrokerProtocol.MaximumFrameCharacters)
        {
            throw new InvalidDataException(
                "The command payload is empty or exceeds the broker frame limit.");
        }
    }

    private static void ThrowIfInvalid(string? validationError)
    {
        if (validationError is not null)
        {
            throw new InvalidDataException(validationError);
        }
    }
}
