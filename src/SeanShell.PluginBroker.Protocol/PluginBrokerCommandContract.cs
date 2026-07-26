using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace SeanShell.PluginBroker.Protocol;

public static class PluginBrokerCommandContract
{
    public const int MaximumQueryCharacters = 256;
    public const int MaximumCommandCount = 32;
    public const int MaximumCommandIdCharacters = 96;
    public const int MaximumTitleCharacters = 120;
    public const int MaximumSubtitleCharacters = 240;
    public const int MaximumKeywordCount = 8;
    public const int MaximumKeywordCharacters = 64;
    public const int MaximumResultMessageCharacters = 512;
    public const int MaximumCommandSetCharacters = 8 * 1024;

    public const string SucceededOutcome = "succeeded";
    public const string FailedOutcome = "failed";
    public const string CancelledOutcome = "cancelled";

    public static string? Validate(PluginBrokerCommandQuery? query)
    {
        if (query is null ||
            !IsDisplayText(query.Query, MaximumQueryCharacters, allowEmpty: true) ||
            query.MaximumResults is <= 0 or > MaximumCommandCount)
        {
            return "The command query is outside the bounded contract.";
        }

        return null;
    }

    public static string? Validate(
        IReadOnlyCollection<PluginBrokerCommandDescriptor>? commands)
    {
        if (commands is null || commands.Count > MaximumCommandCount)
        {
            return "The command descriptor set exceeds its item limit.";
        }

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var totalCharacters = 0;
        foreach (var command in commands)
        {
            if (command is null ||
                !IsCommandId(command.Id) ||
                !ids.Add(command.Id) ||
                !IsDisplayText(command.Title, MaximumTitleCharacters) ||
                !IsOptionalDisplayText(
                    command.Subtitle,
                    MaximumSubtitleCharacters) ||
                command.Keywords is null ||
                command.Keywords.Length > MaximumKeywordCount ||
                command.Keywords.Any(static keyword =>
                    !IsDisplayText(keyword, MaximumKeywordCharacters)) ||
                command.Keywords.Distinct(StringComparer.OrdinalIgnoreCase).Count() !=
                command.Keywords.Length)
            {
                return "The command descriptor set contains an invalid entry.";
            }

            totalCharacters = checked(
                totalCharacters +
                command.Id.Length +
                command.Title.Length +
                (command.Subtitle?.Length ?? 0) +
                command.Keywords.Sum(static keyword => keyword.Length));
            if (totalCharacters > MaximumCommandSetCharacters)
            {
                return "The command descriptor set exceeds its text limit.";
            }
        }

        return null;
    }

    public static string? Validate(PluginBrokerCommandInvocation? invocation)
    {
        if (invocation is null ||
            !IsCommandId(invocation.CommandId) ||
            !PluginBrokerProtocol.IsValidSha256(invocation.CommandSetSha256))
        {
            return "The command invocation is outside the bounded contract.";
        }

        return null;
    }

    public static string? Validate(PluginBrokerCommandResult? result)
    {
        if (result is null ||
            (result.Outcome is not SucceededOutcome and
                not FailedOutcome and
                not CancelledOutcome) ||
            !IsOptionalDisplayText(
                result.Message,
                MaximumResultMessageCharacters))
        {
            return "The command result is outside the bounded contract.";
        }

        return null;
    }

    public static string ComputeCommandSetDigest(
        IReadOnlyCollection<PluginBrokerCommandDescriptor> commands)
    {
        var validationError = Validate(commands);
        if (validationError is not null)
        {
            throw new InvalidDataException(validationError);
        }

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var command in commands.OrderBy(
                     static command => command.Id,
                     StringComparer.OrdinalIgnoreCase))
        {
            Append(hash, command.Id.ToLowerInvariant());
            Append(hash, command.Title);
            Append(hash, command.Subtitle ?? string.Empty);
            Append(hash, command.Keywords.Length.ToString(
                System.Globalization.CultureInfo.InvariantCulture));
            foreach (var keyword in command.Keywords)
            {
                Append(hash, keyword);
            }
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static bool IsCommandId(string? value) =>
        value is { Length: > 0 and <= MaximumCommandIdCharacters } &&
        value.All(static character =>
            char.IsAsciiLetterOrDigit(character) ||
            character is '.' or '-' or '_');

    private static bool IsDisplayText(
        string? value,
        int maximumCharacters,
        bool allowEmpty = false) =>
        value is not null &&
        value.Length <= maximumCharacters &&
        (allowEmpty || value.Length > 0) &&
        value.All(static character => !char.IsControl(character));

    private static bool IsOptionalDisplayText(string? value, int maximumCharacters) =>
        value is null || IsDisplayText(value, maximumCharacters, allowEmpty: true);

    private static void Append(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }
}
