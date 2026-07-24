using System.Diagnostics;

namespace SeanShell.Plugin.Docker;

internal static class DockerCommandStartInfoFactory
{
    internal static ProcessStartInfo CreateLogs(string containerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(containerId);

        var dockerPath = ResolveDockerPath();
        var startInfo = new ProcessStartInfo(dockerPath)
        {
            UseShellExecute = true,
            WorkingDirectory = Environment.GetFolderPath(
                Environment.SpecialFolder.UserProfile),
        };
        startInfo.ArgumentList.Add("logs");
        startInfo.ArgumentList.Add("--tail");
        startInfo.ArgumentList.Add("200");
        startInfo.ArgumentList.Add("--follow");
        startInfo.ArgumentList.Add(containerId);
        return startInfo;
    }

    private static string ResolveDockerPath()
    {
        var candidate = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "Docker",
            "Docker",
            "resources",
            "bin",
            "docker.exe");
        return File.Exists(candidate) ? candidate : "docker.exe";
    }
}
