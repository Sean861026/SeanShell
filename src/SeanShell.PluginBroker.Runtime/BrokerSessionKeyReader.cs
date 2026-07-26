using System.Globalization;
using Microsoft.Win32.SafeHandles;

namespace SeanShell.PluginBroker;

internal static class BrokerSessionKeyReader
{
    private const string HandleArgumentPrefix = "--session-key-handle=";

    public static byte[] Read(string[] arguments)
    {
        if (arguments.Length != 2 ||
            !string.Equals(
                arguments[0],
                PluginBrokerEntryPoint.BrokerModeArgument,
                StringComparison.Ordinal) ||
            !arguments[1].StartsWith(HandleArgumentPrefix, StringComparison.Ordinal) ||
            !long.TryParse(
                arguments[1].AsSpan(HandleArgumentPrefix.Length),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var rawHandle) ||
            rawHandle <= 0)
        {
            throw new InvalidDataException(
                "The broker requires its exact mode and inherited session-key handle.");
        }

        using var handle = new SafeFileHandle(new IntPtr(rawHandle), ownsHandle: true);
        using var stream = new FileStream(
            handle,
            FileAccess.Read,
            bufferSize: PluginBroker.Protocol.PluginBrokerAuthentication.SessionKeyBytes);
        var key = new byte[PluginBroker.Protocol.PluginBrokerAuthentication.SessionKeyBytes];
        try
        {
            stream.ReadExactly(key);
            if (stream.ReadByte() != -1)
            {
                throw new InvalidDataException("The broker session-key frame is oversized.");
            }

            return key;
        }
        catch
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(key);
            throw;
        }
    }
}
