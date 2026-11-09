using System.Runtime.CompilerServices;

namespace Axitor.Trading.Gateway.Core.Concurrency;

/// <summary>Seqlock steps over a sequence field the caller owns: one writer at a time, readers without locks.</summary>
/// <remarks>
/// DESIGN NOTE: The sequence is odd while a write is in progress. Starting a write is a full fence, so a reader never sees new bytes
/// under an old even sequence; ending it is a release, so the new data is visible before the even sequence.
/// A reader fences after its copy, so the copy is complete before it checks the sequence again. This also holds on ARM.
/// </remarks>
internal static class SeqLock
{
    /// <summary>Marks a write in progress. Writes must never overlap.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void BeginWrite(ref long sequence) => _ = Interlocked.Exchange(ref sequence, sequence + 1);

    /// <summary>Marks the write done.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void EndWrite(ref long sequence) => Volatile.Write(ref sequence, sequence + 1);

    /// <summary>Starts a read: waits out a write in progress and returns the sequence to pass to EndRead.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long BeginRead(ref long sequence)
    {
        long start = Volatile.Read(ref sequence);
        return (start & 1) == 0 ? start : WaitForWrite(ref sequence);
    }

    /// <summary>Ends a read. True when no write happened since BeginRead, so the copy is consistent; otherwise read again.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool EndRead(ref long sequence, long start)
    {
        Interlocked.MemoryBarrier();
        return Volatile.Read(ref sequence) == start;
    }

    /// <summary>Writes completed so far.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long ReadVersion(ref long sequence) => ToVersion(Volatile.Read(ref sequence));

    /// <summary>Writes completed as of a sequence value returned by BeginRead.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long ToVersion(long sequence) => sequence >> 1;

    /// <summary>Cold path. A write takes nanoseconds: spin, never sleep.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static long WaitForWrite(ref long sequence)
    {
        SpinWait spinner = default;

        while (true)
        {
            spinner.SpinOnce(sleep1Threshold: -1);
            long start = Volatile.Read(ref sequence);

            if ((start & 1) == 0)
            {
                return start;
            }
        }
    }
}
