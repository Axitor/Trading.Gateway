namespace Axitor.Trading.Gateway.Core.Concurrency;

/// <summary>Latest value from one writer, read by any thread without locks.</summary>
internal sealed class SeqLockSlot<T> where T : struct
{
    private T _value;
    private long _sequence;

    /// <summary>Writes completed so far. A cheap check whether anything changed.</summary>
    public long Version => SeqLock.ReadVersion(ref _sequence);

    /// <summary>Stores a new value. Writes must never overlap.</summary>
    public void Write(in T value)
    {
        SeqLock.BeginWrite(ref _sequence);
        _value = value;
        SeqLock.EndWrite(ref _sequence);
    }

    /// <summary>Copies the latest value and returns how many writes it reflects. Zero means nothing was written yet.</summary>
    public long Read(out T value)
    {
        while (true)
        {
            long start = SeqLock.BeginRead(ref _sequence);
            value = _value;

            if (SeqLock.EndRead(ref _sequence, start))
            {
                return SeqLock.ToVersion(start);
            }
        }
    }
}
