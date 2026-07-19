namespace SeanShell.Core;

public sealed class ShellStateStore
{
    private readonly object _gate = new();
    private ShellState _current = ShellState.Initial;

    public event EventHandler<ShellState>? StateChanged;

    public ShellState Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public bool SetMode(ShellMode mode)
    {
        ShellState next;

        lock (_gate)
        {
            if (_current.Mode == mode)
            {
                return false;
            }

            next = new ShellState(mode, DateTimeOffset.UtcNow);
            _current = next;
        }

        StateChanged?.Invoke(this, next);
        return true;
    }
}
