using System.ComponentModel;
using System.Diagnostics;
using SeanShell.Core;

namespace SeanShell.Windows;

public sealed class ProcessCatalog
{
    public IReadOnlyList<ProcessSnapshot> Capture()
    {
        var observedAt = DateTimeOffset.UtcNow;

        var snapshots = new List<ProcessSnapshot>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    snapshots.Add(new ProcessSnapshot(process.Id, process.ProcessName, observedAt));
                }
                catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
                {
                    // Processes may exit or become inaccessible while the snapshot is collected.
                }
            }
        }

        return snapshots
            .OrderBy(static process => process.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static process => process.Id)
            .ToArray();
    }
}
