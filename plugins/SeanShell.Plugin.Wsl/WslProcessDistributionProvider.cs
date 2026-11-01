using System.Diagnostics;
using System.Text;
using SeanShell.Core;

namespace SeanShell.Plugin.Wsl;

public sealed class WslProcessDistributionProvider : IWslDistributionProvider
{
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(10);
    private readonly Func<ProcessStartInfo, TimeSpan, CancellationToken, Task<ProcessExecutionResult>> _runAsync;

    public WslProcessDistributionProvider() : this(BoundedProcessRunner.RunAsync)
    {
    }

    internal WslProcessDistributionProvider(
        Func<ProcessStartInfo, TimeSpan, CancellationToken, Task<ProcessExecutionResult>> runAsync)
    {
        _runAsync = runAsync;
    }

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

        try
        {
            var result = await _runAsync(startInfo, QueryTimeout, cancellationToken).ConfigureAwait(false);
            return result.ExitCode == 0
                ? WslDistributionParser.Parse(result.StandardOutput)
                : [];
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (TimeoutException)
        {
            throw;
        }
        catch
        {
            return [];
        }
    }
}
