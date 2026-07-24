using System.Diagnostics;

namespace SeanShell.Plugin.Wsl;

internal static class WslShellStartInfoFactory
{
    internal static ProcessStartInfo Create(string distributionName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(distributionName);

        var systemDirectory = Environment.GetFolderPath(
            Environment.SpecialFolder.System);
        var userProfile = Environment.GetFolderPath(
            Environment.SpecialFolder.UserProfile);
        var startInfo = new ProcessStartInfo(
            Path.Combine(systemDirectory, "wsl.exe"))
        {
            UseShellExecute = true,
            WorkingDirectory = userProfile,
        };
        startInfo.ArgumentList.Add("--distribution");
        startInfo.ArgumentList.Add(distributionName);
        return startInfo;
    }
}
