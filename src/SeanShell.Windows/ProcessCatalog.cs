using System.Diagnostics;
using SeanShell.Core;

namespace SeanShell.Windows;

public sealed class ProcessCatalog
{
    public IReadOnlyList<ProcessSnapshot> Capture()
    {
        var observedAt = DateTimeOffset.UtcNow;

        return Process.GetProcesses()
            .Select(process =>
            {
                using (process)
                {
                    return new ProcessSnapshot(process.Id, process.ProcessName, observedAt);
                }
            })
            .OrderBy(static process => process.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static process => process.Id)
            .ToArray();
    }
}
