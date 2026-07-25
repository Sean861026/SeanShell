using System.Diagnostics;

namespace SeanShell.Windows;

public static class ExplorerRecoveryService
{
    public static bool EnsureRunning()
    {
        var explorerProcesses = Process.GetProcessesByName("explorer");
        try
        {
            if (explorerProcesses.Length > 0)
            {
                return false;
            }
        }
        finally
        {
            foreach (var process in explorerProcesses)
            {
                process.Dispose();
            }
        }

        var windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        using var started = Process.Start(
            new ProcessStartInfo
            {
                FileName = Path.Combine(windowsDirectory, "explorer.exe"),
                UseShellExecute = true,
            });
        return started is not null;
    }
}
