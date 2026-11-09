using System.Runtime.CompilerServices;
using Axitor.Trading.Gateway.Core.Concurrency;

namespace Axitor.Trading.Gateway.Tests.Core.Concurrency;

public sealed class SeqLockSlotTests
{
    private const int Writes = 2_000_000;
    private const int Readers = 3;
    private const int StartTimeoutMillis = 10_000;

    [Fact]
    public void ReadsNothingBeforeTheFirstWrite()
    {
        SeqLockSlot<Snapshot> slot = new();

        Assert.Equal(0, slot.Read(out Snapshot value));
        Assert.Equal(0, value.First);
        Assert.Equal(0, slot.Version);
    }

    [Fact]
    public async Task ReadersNeverSeeAHalfWrittenValue()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        SeqLockSlot<Snapshot> slot = new();
        StrongBox<int> started = new();
        StrongBox<int> done = new();

        Task<ReadStats>[] readers = new Task<ReadStats>[Readers];

        for (int i = 0; i < readers.Length; i++)
        {
            readers[i] = Task.Factory.StartNew(() => Read(slot, started, done), cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        await Task.Factory.StartNew(() => Write(slot, started), cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        Volatile.Write(ref done.Value, 1);

        long reads = 0;
        long torn = 0;
        long backwards = 0;

        foreach (ReadStats stats in await Task.WhenAll(readers))
        {
            reads += stats.Reads;
            torn += stats.Torn;
            backwards += stats.Backwards;
        }

        Assert.Equal(0, torn);
        Assert.Equal(0, backwards);
        Assert.True(0 < reads);
        Assert.Equal(Writes, slot.Read(out Snapshot last));
        Assert.Equal(Writes, last.Fourth);
    }

    private static void Write(SeqLockSlot<Snapshot> slot, StrongBox<int> started)
    {
        // Every reader runs before the first write, so reads overlap the writes.
        SpinWait.SpinUntil(() => Volatile.Read(ref started.Value) == Readers, StartTimeoutMillis);

        for (long i = 1; i <= Writes; i++)
        {
            slot.Write(new Snapshot() { First = i, Second = i, Third = i, Fourth = i });
        }
    }

    private static ReadStats Read(SeqLockSlot<Snapshot> slot, StrongBox<int> started, StrongBox<int> done)
    {
        ReadStats stats = default;
        long lastVersion = 0;

        Interlocked.Increment(ref started.Value);

        while (Volatile.Read(ref done.Value) == 0)
        {
            long version = slot.Read(out Snapshot value);
            stats.Reads++;

            // Write n puts n into every field.
            if (value.First != version || value.Second != version || value.Third != version || value.Fourth != version)
            {
                stats.Torn++;
            }

            if (version < lastVersion)
            {
                stats.Backwards++;
            }

            lastVersion = version;
        }

        return stats;
    }

    /// <summary>Wider than any atomic store, so a torn read would show.</summary>
    private struct Snapshot
    {
        public long First;
        public long Second;
        public long Third;
        public long Fourth;
    }

    private struct ReadStats
    {
        public long Reads;
        public long Torn;
        public long Backwards;
    }
}
