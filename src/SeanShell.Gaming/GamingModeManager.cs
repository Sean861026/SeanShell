using SeanShell.Core;

namespace SeanShell.Gaming;

public sealed class GamingModeManager
{
    private readonly Dictionary<int, string> _activeGames = [];
    private readonly ShellStateStore _stateStore;
    private GameDetector _detector = new([]);
    private bool _automaticDetectionEnabled;
    private bool _manualModeEnabled;

    public GamingModeManager(ShellStateStore stateStore)
    {
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
    }

    public event EventHandler<GamingModeStatus>? StatusChanged;

    public GamingModeStatus Current => CreateStatus();

    public void ConfigureAutomaticDetection(bool enabled, IEnumerable<string> processNames)
    {
        ArgumentNullException.ThrowIfNull(processNames);
        _automaticDetectionEnabled = enabled;
        _detector = new GameDetector(processNames);
        _activeGames.Clear();
        ApplyState(statusChanged: true);
    }

    public void SetManualMode(bool enabled)
    {
        if (_manualModeEnabled == enabled)
        {
            return;
        }

        _manualModeEnabled = enabled;
        ApplyState(statusChanged: true);
    }

    public void Reconcile(IEnumerable<ProcessSnapshot> processes)
    {
        ArgumentNullException.ThrowIfNull(processes);
        var detected = _automaticDetectionEnabled
            ? processes
                .Where(process => _detector.IsGame(process.Name))
                .GroupBy(static process => process.Id)
                .ToDictionary(static group => group.Key, static group => group.First().Name)
            : [];

        if (HasSameGames(detected))
        {
            return;
        }

        _activeGames.Clear();
        foreach (var game in detected)
        {
            _activeGames.Add(game.Key, game.Value);
        }

        ApplyState(statusChanged: true);
    }

    private bool HasSameGames(IReadOnlyDictionary<int, string> detected) =>
        detected.Count == _activeGames.Count && detected.All(game =>
            _activeGames.TryGetValue(game.Key, out var name) &&
            string.Equals(name, game.Value, StringComparison.OrdinalIgnoreCase));

    private void ApplyState(bool statusChanged)
    {
        var status = CreateStatus();
        _stateStore.SetMode(status.IsGaming ? ShellMode.Gaming : ShellMode.Normal);
        if (statusChanged)
        {
            StatusChanged?.Invoke(this, status);
        }
    }

    private GamingModeStatus CreateStatus() => new(
        _manualModeEnabled,
        _automaticDetectionEnabled,
        _detector.RuleCount,
        _activeGames.Values
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray());
}
