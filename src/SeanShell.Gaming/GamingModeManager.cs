using SeanShell.Core;

namespace SeanShell.Gaming;

public sealed class GamingModeManager(ShellStateStore stateStore, GameDetector detector)
{
    private readonly HashSet<int> _activeGameProcessIds = [];

    public void ProcessStarted(ProcessSnapshot process)
    {
        if (detector.IsGame(process.Name) && _activeGameProcessIds.Add(process.Id))
        {
            stateStore.SetMode(ShellMode.Gaming);
        }
    }

    public void ProcessStopped(int processId)
    {
        if (_activeGameProcessIds.Remove(processId) && _activeGameProcessIds.Count == 0)
        {
            stateStore.SetMode(ShellMode.Normal);
        }
    }
}
