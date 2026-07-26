using System.Security.Cryptography;
using System.Text;

namespace SeanShell.PluginBroker.Protocol;

public static class PluginBrokerAuthentication
{
    public const int SessionKeyBytes = 32;
    public const int NonceBytes = 32;

    public static byte[] CreateSessionKey() =>
        RandomNumberGenerator.GetBytes(SessionKeyBytes);

    public static string CreateSessionId() =>
        Guid.NewGuid().ToString("N");

    public static string CreateNonce() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(NonceBytes));

    public static PluginBrokerRequest SignRequest(
        PluginBrokerRequest request,
        ReadOnlySpan<byte> sessionKey)
    {
        ValidateSessionKey(sessionKey);
        var unsigned = request with { AuthenticationTag = null };
        return unsigned with
        {
            AuthenticationTag = ComputeTag(
                PluginBrokerProtocol.Serialize(unsigned),
                sessionKey),
        };
    }

    public static bool VerifyRequest(
        PluginBrokerRequest request,
        ReadOnlySpan<byte> sessionKey)
    {
        ValidateSessionKey(sessionKey);
        return HasValidEnvelope(request.SessionId, request.Nonce, request.AuthenticationTag) &&
               VerifyTag(
                   PluginBrokerProtocol.Serialize(request with { AuthenticationTag = null }),
                   request.AuthenticationTag!,
                   sessionKey);
    }

    public static PluginBrokerResponse SignResponse(
        PluginBrokerResponse response,
        ReadOnlySpan<byte> sessionKey)
    {
        ValidateSessionKey(sessionKey);
        var unsigned = response with { AuthenticationTag = null };
        return unsigned with
        {
            AuthenticationTag = ComputeTag(
                PluginBrokerProtocol.Serialize(unsigned),
                sessionKey),
        };
    }

    public static bool VerifyResponse(
        PluginBrokerResponse response,
        ReadOnlySpan<byte> sessionKey)
    {
        ValidateSessionKey(sessionKey);
        return HasValidEnvelope(response.SessionId, response.Nonce, response.AuthenticationTag) &&
               VerifyTag(
                   PluginBrokerProtocol.Serialize(response with { AuthenticationTag = null }),
                   response.AuthenticationTag!,
                   sessionKey);
    }

    private static bool HasValidEnvelope(
        string? sessionId,
        string? nonce,
        string? authenticationTag) =>
        PluginBrokerProtocol.IsValidRequestId(sessionId) &&
        PluginBrokerProtocol.IsValidSha256(nonce) &&
        PluginBrokerProtocol.IsValidSha256(authenticationTag);

    private static string ComputeTag(string frame, ReadOnlySpan<byte> sessionKey)
    {
        var data = Encoding.UTF8.GetBytes(frame);
        var tag = HMACSHA256.HashData(sessionKey, data);
        try
        {
            return Convert.ToHexString(tag);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(data);
            CryptographicOperations.ZeroMemory(tag);
        }
    }

    private static bool VerifyTag(
        string frame,
        string authenticationTag,
        ReadOnlySpan<byte> sessionKey)
    {
        byte[] suppliedTag;
        try
        {
            suppliedTag = Convert.FromHexString(authenticationTag);
        }
        catch (FormatException)
        {
            return false;
        }

        var data = Encoding.UTF8.GetBytes(frame);
        var expectedTag = HMACSHA256.HashData(sessionKey, data);
        try
        {
            return CryptographicOperations.FixedTimeEquals(expectedTag, suppliedTag);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(data);
            CryptographicOperations.ZeroMemory(expectedTag);
            CryptographicOperations.ZeroMemory(suppliedTag);
        }
    }

    private static void ValidateSessionKey(ReadOnlySpan<byte> sessionKey)
    {
        if (sessionKey.Length != SessionKeyBytes)
        {
            throw new ArgumentException(
                $"Broker session keys must contain exactly {SessionKeyBytes} bytes.",
                nameof(sessionKey));
        }
    }
}
