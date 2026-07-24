using System.Diagnostics;
using System.Text;

namespace SeanShell.Plugin.Wsl;

public sealed class WslProcessDistributionProvider : IWslDistributionProvider
{
    public async ValueTask<IReadOnlyList<WslDistributionSnapshot>> GetDistributionsAsync(
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("wsl.exe")
        {
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            StandardErrorEncoding = Encoding.Unicode,
            StandardOutputEncoding = Encoding.Unicode,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("--list");
        startInfo.ArgumentList.Add("--verbose");

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                return [];
            }

            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var output = await outputTask.ConfigureAwait(false);
            _ = await errorTask.ConfigureAwait(false);
            return process.ExitCode == 0
                ? WslDistributionParser.Parse(output)
                : [];
        }
        catch (OperationCanceledException)
        {
            TryTerminate(process);
            throw;
        }
        catch
        {
            TryTerminate(process);
            return [];
        }
    }

    private static void TryTerminate(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // The process may have already exited or become inaccessible.
        }
    }
}
