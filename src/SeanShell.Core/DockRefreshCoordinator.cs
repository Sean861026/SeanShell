namespace SeanShell.Core;

/// <summary>
/// Coalesces background Dock scans while serializing user-requested scans.
/// An explicit request waits for the current scan instead of using its result:
/// the caller must invalidate its snapshot cache after acquiring the lease.
/// </summary>
public sealed class DockRefreshCoordinator
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<Lease?> EnterAsync(
        bool explicitlyRequested,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!explicitlyRequested)
        {
            return await _gate.WaitAsync(0, cancellationToken).ConfigureAwait(false)
                ? new Lease(_gate)
                : null;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Lease(_gate);
    }

    public sealed class Lease : IDisposable
    {
        private SemaphoreSlim? _gate;

        internal Lease(SemaphoreSlim gate) => _gate = gate;

        public void Dispose() => Interlocked.Exchange(ref _gate, null)?.Release();
    }
}
