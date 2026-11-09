using System.Diagnostics;

namespace Axitor.Trading.Gateway.Benchmarks.Streams;

/// <summary>Counts messages and keeps the latency of each one: now minus its event time, on the stub's clock in microseconds.</summary>
internal sealed class LatencyCollector
{
    // Touching one element per 4 KB page commits the array before the run.
    private const int LongsPerPage = 4096 / sizeof(long);

    // Same formula as the stub, so both sides round alike.
    private static readonly double _microsPerTick = 1_000_000.0 / Stopwatch.Frequency;

    private readonly long[] _samples;
    private long _received;
    private long _firstTicks;
    private long _lastTicks;
    private long _gaps;
    private long _resyncs;
    private int _sampleCount;
    private int _isSampling;

    public LatencyCollector(int capacity)
    {
        _samples = new long[capacity];

        for (int i = 0; i < capacity; i += LongsPerPage)
        {
            _samples[i] = 0;
        }
    }

    public long Received => Volatile.Read(ref _received);

    /// <summary>Stopwatch timestamp of the first message, or zero before it.</summary>
    public long FirstTicks => Volatile.Read(ref _firstTicks);

    /// <summary>Stopwatch timestamp of the latest message.</summary>
    public long LastTicks => Volatile.Read(ref _lastTicks);

    /// <summary>Connection breaks seen by the handlers.</summary>
    public long Gaps => Volatile.Read(ref _gaps);

    /// <summary>Times a synced local order book lost its sync.</summary>
    public long Resyncs => Volatile.Read(ref _resyncs);

    /// <summary>Samples kept; at most the capacity.</summary>
    public int SampleCount => Math.Min(Volatile.Read(ref _sampleCount), _samples.Length);

    /// <summary>Whether more messages were sampled than the capacity holds.</summary>
    public bool IsTruncated => _samples.Length < Volatile.Read(ref _sampleCount);

    /// <summary>The stub's clock: machine-wide monotonic microseconds.</summary>
    public static long NowMicros() => (long)(Stopwatch.GetTimestamp() * _microsPerTick);

    /// <summary>One message with its event time in stub microseconds. Zero or less counts it without a sample.</summary>
    public void Record(long eventMicros)
    {
        long ticks = Stopwatch.GetTimestamp();
        Mark(ticks);

        if (eventMicros <= 0 || Volatile.Read(ref _isSampling) == 0)
        {
            return;
        }

        int index = Interlocked.Increment(ref _sampleCount) - 1;

        if ((uint)index < (uint)_samples.Length)
        {
            _samples[index] = (long)(ticks * _microsPerTick) - eventMicros;
        }
    }

    /// <summary>One message that carries no event time.</summary>
    public void Count() => Mark(Stopwatch.GetTimestamp());

    public void AddGap() => Interlocked.Increment(ref _gaps);

    public void AddResync() => Interlocked.Increment(ref _resyncs);

    /// <summary>Starts keeping samples; messages before it are the warmup.</summary>
    public void StartSampling() => Volatile.Write(ref _isSampling, 1);

    /// <summary>Sorts the kept samples in place. Call once the run is over.</summary>
    public ReadOnlySpan<long> SortSamples()
    {
        Span<long> samples = _samples.AsSpan(0, SampleCount);
        samples.Sort();
        return samples;
    }

    /// <summary>Nearest-rank percentile of sorted samples, e.g. 0.99.</summary>
    public static long Percentile(ReadOnlySpan<long> sorted, double fraction)
    {
        int rank = (int)Math.Ceiling(fraction * sorted.Length);
        return sorted[Math.Clamp(rank - 1, 0, sorted.Length - 1)];
    }

    private void Mark(long ticks)
    {
        if (Volatile.Read(ref _firstTicks) == 0)
        {
            Interlocked.CompareExchange(ref _firstTicks, ticks, 0);
        }

        Volatile.Write(ref _lastTicks, ticks);
        Interlocked.Increment(ref _received);
    }
}
