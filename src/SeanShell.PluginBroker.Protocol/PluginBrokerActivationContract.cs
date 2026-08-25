namespace SeanShell.PluginBroker.Protocol;

public static class PluginBrokerActivationContract
{
    public const int MaximumEntryTypeCharacters = 256;

    public static string? Validate(
        PluginBrokerActivationRequest? request,
        PluginBrokerGrant? grant)
    {
        if (request is null ||
            grant is null ||
            !IsValidEntryType(request.EntryType) ||
            !IsValidEntryType(grant.EntryType) ||
            !string.Equals(
                request.EntryType,
                grant.EntryType,
                StringComparison.Ordinal))
        {
            return "The plugin activation entry type must exactly match the short-lived grant.";
        }

        if (!IsCapabilitySet(grant.GrantedCapabilities) ||
            !IsCapabilitySet(request.RequestedCapabilities) ||
            (request.RequestedCapabilities & grant.GrantedCapabilities) !=
            request.RequestedCapabilities)
        {
            return "Plugin activation capabilities must be a non-empty subset of the short-lived grant.";
        }

        return null;
    }

    public static bool IsValidEntryType(string? value)
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
