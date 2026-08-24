namespace SeanShell.PluginBroker.Protocol;

public static class PluginBrokerActivationContract
{
    public const int MaximumEntryTypeCharacters = 256;

    public static string? Validate(
        PluginBrokerActivationRequest? request,
        int grantedCapabilities)
    {
        if (request is null || !IsEntryType(request.EntryType))
        {
            return "The plugin activation entry type is invalid.";
        }

        if (!IsCapabilitySet(grantedCapabilities) ||
            !IsCapabilitySet(request.RequestedCapabilities) ||
            (request.RequestedCapabilities & grantedCapabilities) !=
            request.RequestedCapabilities)
        {
            return "Plugin activation capabilities must be a non-empty subset of the short-lived grant.";
        }

        return null;
    }

    private static bool IsEntryType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length > MaximumEntryTypeCharacters)
        {
            return false;
        }

        var segments = value.Split('.', StringSplitOptions.None);
        return segments.Length > 1 && segments.All(IsIdentifier);
    }

    private static bool IsIdentifier(string segment) =>
        segment.Length > 0 &&
        IsIdentifierStart(segment[0]) &&
        segment.Skip(1).All(static character =>
            IsIdentifierStart(character) || char.IsAsciiDigit(character));

    private static bool IsIdentifierStart(char character) =>
        char.IsAsciiLetter(character) || character == '_';

    private static bool IsCapabilitySet(int capabilities) =>
        capabilities != 0 &&
        (capabilities & ~PluginBrokerProtocol.KnownCapabilityMask) == 0;
}
