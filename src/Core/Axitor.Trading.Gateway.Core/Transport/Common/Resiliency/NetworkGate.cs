using System.Runtime.CompilerServices;

namespace Axitor.Trading.Gateway.Core.Transport.Common.Resiliency;

/// <summary>Circuit breaker for a transiently down network. Repair is driven externally.</summary>
internal sealed class NetworkGate
{
    private const int StateHealthy = 0;
    private const int StateDown = 1;

    private volatile TaskCompletionSource _repairTcs;
    private TaskCompletionSource _nextRepairSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Lock _stateLock = new();
    private int _state = StateHealthy;
    private int _repairGeneration;

    public NetworkGate()
    {
        _repairTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _repairTcs.SetResult();
    }

    /// <summary>Goes up by one each time the gate turns healthy again.</summary>
    public int RepairGeneration => Volatile.Read(ref _repairGeneration);

    /// <summary>Completes on the next repair. Read it before RepairGeneration, so a repair in between is seen by one of them.</summary>
    public Task NextRepair => Volatile.Read(ref _nextRepairSource).Task;

    /// <summary>Lock-free state read for the hot path.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsHealthy() => Volatile.Read(ref _state) == StateHealthy;

    /// <summary>Completes at once when healthy, otherwise waits for repair.</summary>
    public async ValueTask<bool> WaitUntilHealthyAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _state) == StateHealthy) return false;

        // Take the TCS first, then re-check state. Reverse order to MarkDown, so a heal
        // landing in between reads as healthy here instead of parking us on a stale TCS.
        TaskCompletionSource tcs = _repairTcs;

        if (Volatile.Read(ref _state) == StateHealthy) return false;

        await tcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>Trips the gate. True only for the thread that wins the transition.</summary>
    public bool MarkDown()
    {
        if (Volatile.Read(ref _state) != StateHealthy) return false;

        lock (_stateLock)
        {
            if (_state != StateHealthy) return false;

            _repairTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Volatile.Write(ref _state, StateDown);
            return true;
        }
    }

    /// <summary>Restores the gate, releases everyone waiting on repair and signals the repair.</summary>
    public void MarkHealthy()
    {
        if (Volatile.Read(ref _state) == StateHealthy) return;

        lock (_stateLock)
        {
            if (_state != StateDown) return;

            // Generation first: whoever sees the gate healthy or wakes on either signal already reads the new one,
            // so a connection opened after the repair is not rotated again.
            Interlocked.Increment(ref _repairGeneration);
            Volatile.Write(ref _state, StateHealthy);
            _repairTcs.TrySetResult();

            TaskCompletionSource next = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Interlocked.Exchange(ref _nextRepairSource, next).TrySetResult();
        }
    }
}
