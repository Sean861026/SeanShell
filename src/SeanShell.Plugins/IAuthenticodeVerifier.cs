namespace SeanShell.Plugins;

public interface IAuthenticodeVerifier
{
    AuthenticodeVerificationResult Verify(string filePath);
}
